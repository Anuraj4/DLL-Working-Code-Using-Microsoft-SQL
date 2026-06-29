using System;
using System.Collections.Generic;
using System.Linq;
using EdiFabric.Core.Model.Edi;
using EdiFabric.Core.Model.Edi.X12;
using EdiFabric.Templates.Hipaa5010;
using Edi.Generator835.Configuration;
using Edi.Generator835.Context;
using Edi.Generator835.Models;
using Edi.Generator835.Rules;
using Serilog;

namespace Edi.Generator835.Generators
{
    public class Edi835Generator : IEdi835Generator
    {
        private readonly RuleExecutionEngine _engine;
        private readonly GenerationContext _context;
        private readonly MappingConfiguration _mappings;
        private readonly Xalta.Edi.CodeCrossWalk.Interfaces.ICodeCrossWalkService? _crossWalkService;
        private Edi835DataModel? _currentDataModel;

        public Edi835Generator(RuleExecutionEngine engine, GenerationContext context, MappingConfiguration mappings, Xalta.Edi.CodeCrossWalk.Interfaces.ICodeCrossWalkService? crossWalkService = null)
        {
            _engine = engine;
            _context = context;
            _mappings = mappings;
            _crossWalkService = crossWalkService;
        }

        public TS835 Generate(Edi835DataModel data)
        {
            Log.Information("Starting EDI 835 transaction generation for Payment ID: {PaymentId}", data.Header.PaymentId);
            _currentDataModel = data;

            // 1. Identify Payer from Registry
            _mappings.MatchPayer(data.Header.PayerName);
            string payerIdFromRegistry = data.Header.PayerId;
            if (_mappings.MatchedPayer != null)
            {
                var pId = _mappings.GetPayerId(_mappings.MatchedPayer);
                if (!string.IsNullOrEmpty(pId)) payerIdFromRegistry = pId;
            }

            string tin = data.Header.PayerId;
            if (_mappings.MatchedPayer != null)
            {
                var pTin = _mappings.GetPayerTin(_mappings.MatchedPayer);
                if (!string.IsNullOrEmpty(pTin)) tin = pTin;
            }

            Log.Information("[Identification] Resolved Payer ID: '{PayerID}', TIN: '{TIN}' (Registry Match: {IsMatched})",
                payerIdFromRegistry, tin, _mappings.MatchedPayer != null);

            var transaction = new TS835();

            // ST - Transaction Set Header
            transaction.ST = new ST
            {
                TransactionSetIdentifierCode_01 = GetFixedDefault("ST_TransactionSetIdentifierCode", "835"),
                TransactionSetControlNumber_02 = _context.ControlNumbers.NextTransactionControlNumber()
            };

            // BPR - Financial Information
            transaction.BPR_FinancialInformation = BuildBPR(data.Header, tin);

            // TRN - Reassociation Trace Number
            var trnRow = ToRowData(data.Header);
            transaction.TRN_ReassociationTraceNumber = new TRN_DependentTraceNumber
            {
                TraceTypeCode_01 = GetFixedDefault("TRN01_TraceTypeCode", "1"),
                CurrentTransactionTraceNumber_02 = DetermineValue("TRN02", data.Header.CheckOrEftNumber, trnRow, data.Header.PaymentId),
                OriginatingCompanyIdentifier_03 = DetermineValue("TRN03", tin, trnRow, "9999999999")
            };

            // CUR - Currency Information (Conditional)
            if (!string.IsNullOrEmpty(data.Header.CurrencyCode) && data.Header.CurrencyCode != "USD")
            {
                transaction.CUR_ForeignCurrencyInformation = BuildCUR(data.Header);
            }

            // DTM - Production Date
            transaction.DTM_ProductionDate = new DTM_ProductionDate
            {
                DateTimeQualifier_01 = GetFixedDefault("DTM01_ProductionDate", "405"),
                Date_02 = FormatDate(_context.ProcessingDate.ToString("yyyy-MM-dd"))
            };

            // Loops
            Log.Debug("Building Loops 1000A (Payer) and 1000B (Payee)...");
            transaction.AllN1 = new All_N1_835
            {
                Loop1000A = BuildLoop1000A(data.Header),
                Loop1000B = BuildLoop1000B(data.Header)
            };

            Log.Debug("Building Loop 2000 (Header/Claims)...");
            transaction.Loop2000 = new List<Loop_2000_835> { BuildLoop2000(data) };

            if (data.ProviderAdjustments.Any())
            {
                transaction.PLB_ProviderAdjustment = BuildPLB(data.ProviderAdjustments);
            }

            // SE - Transaction Set Trailer
            transaction.SE = new SE
            {
                NumberofIncludedSegments_01 = CalculateSegmentCount(transaction).ToString(),
                TransactionSetControlNumber_02 = transaction.ST.TransactionSetControlNumber_02
            };

            Log.Information("EDI 835 structure generation complete for {PaymentId}.", data.Header.PaymentId);
            return transaction;
        }

        private BPR_FinancialInformation_2 BuildBPR(HeaderData header, string tin)
        {
            var rowData = ToRowData(header);
            var bpr = new BPR_FinancialInformation_2();

            // BPR01/03/04: When payment amount is zero, use "H" (Notification Only), "C" (Credit), and "NON" (Non-Payment)
            bool isZeroPayment = header.TotalPaymentAmount == 0m;
            bpr.TransactionHandlingCode_01 = isZeroPayment
                ? GetFixedDefault("BPR01_ZeroPayment", "H")
                : DetermineValue("BPR01", "C", rowData, "C");
            bpr.TotalPremiumPaymentAmount_02 = header.TotalPaymentAmount.ToString("F2");
            bpr.CreditorDebitFlagCode_03 = isZeroPayment
                ? GetFixedDefault("BPR03_ZeroPayment", "C")
                : DetermineValue("BPR03", "C", rowData, "C");
            bpr.PaymentMethodCode_04 = isZeroPayment
                ? GetFixedDefault("BPR04_ZeroPayment", "NON")
                : DetermineValue("BPR04", header.PaymentMethod, rowData, "ACH");
            bpr.PaymentFormatCode_05 = DetermineValue("BPR05", "CCP", rowData, "CCP");

            if (bpr.PaymentMethodCode_04 == "ACH" || bpr.PaymentMethodCode_04 == "CHK")
            {
                // BPR06/07 - Depository Financial Institution
                var routingValue = DetermineValue("BPR07", header.RoutingNumber, rowData);
                if (!string.IsNullOrEmpty(routingValue))
                {
                    bpr.DepositoryFinancialInstitutionDFIIdentificationNumberQualifier_06 = DetermineValue("BPR06", "01", rowData, "01");
                    bpr.OriginatingDepositoryFinancialInstitutionDFIIdentifier_07 = routingValue;
                }

                // BPR08/09 - Account Number
                var accountValue = DetermineValue("BPR09", header.BankAccountNumber, rowData);
                if (!string.IsNullOrEmpty(accountValue))
                {
                    bpr.AccountNumberQualifier_08 = DetermineValue("BPR08", "DA", rowData, "DA");
                    bpr.SenderBankAccountNumber_09 = accountValue;
                }

                // BPR10 - Payer Identifier - Used only when BPR04 is ACH
                if (bpr.PaymentMethodCode_04 == "ACH")
                {
                    var payerIdValue = DetermineValue("TRN03", !string.IsNullOrEmpty(tin) ? tin : header.PayerId, rowData);
                    if (!string.IsNullOrEmpty(payerIdValue))
                    {
                        bpr.PayerIdentifier_10 = payerIdValue;
                    }
                }

                // BPR12/13 - Receiver DFI
                var receiverRoutingValue = DetermineValue("BPR13", header.RoutingNumber, rowData);
                if (!string.IsNullOrEmpty(receiverRoutingValue))
                {
                    bpr.DepositoryFinancialInstitutionDFIIdentificationNumberQualifier_12 = DetermineValue("BPR12", "01", rowData, "01");
                    bpr.ReceivingDepositoryFinancialInstitutionDFIIdentifier_13 = receiverRoutingValue;
                }

                // BPR14/15 - Receiver Account
                var receiverAccountValue = DetermineValue("BPR15", header.BankAccountNumber, rowData);
                if (!string.IsNullOrEmpty(receiverAccountValue))
                {
                    bpr.AccountNumberQualifier_14 = DetermineValue("BPR14", "DA", rowData, "DA");
                    bpr.ReceiverBankAccountNumber_15 = receiverAccountValue;
                }
            }

            bpr.CheckIssueorEFTEffectiveDate_16 = DetermineValue("BPR16", FormatDate(header.PaymentDate), rowData, FormatDate(header.PaymentDate));

            return bpr;
        }

        private CUR_ForeignCurrencyInformation_2 BuildCUR(HeaderData header)
        {
            var rowData = ToRowData(header);
            return new CUR_ForeignCurrencyInformation_2
            {
                EntityIdentifierCode_01 = GetFixedDefault("CUR01", "PR"),
                CurrencyCode_02 = header.CurrencyCode
            };
        }

        private Loop_1000A_835 BuildLoop1000A(HeaderData header)
        {
            var loop = new Loop_1000A_835();
            var rowData = ToRowData(header);
            var payerIdValue = DetermineValue("N104_Payer", header.PayerId, rowData);
            loop.N1_PayerIdentification = new N1_PayerIdentification
            {
                EntityIdentifierCode_01 = GetFixedDefault("N101_Payer", "PR"),
                PremiumPayerName_02 = header.PayerName
            };

            if (!string.IsNullOrEmpty(payerIdValue))
            {
                loop.N1_PayerIdentification.IdentificationCodeQualifier_03 = GetFixedDefault("N103_PayerIdentificationCodeQualifier", "XV");
                loop.N1_PayerIdentification.IntermediaryBankIdentifier_04 = payerIdValue;
            }

            if (!string.IsNullOrEmpty(header.PayerAddressLine1))
            {
                loop.N3_PayerAddress = new N3_AdditionalPatientInformationContactAddress { ResponseContactAddressLine_01 = header.PayerAddressLine1 };
            }

            if (!string.IsNullOrEmpty(header.PayerCity))
            {
                loop.N4_PayerCity_State_ZIPCode = new N4_AdditionalPatientInformationContactCity
                {
                    AdditionalPatientInformationContactCityName_01 = header.PayerCity,
                    AdditionalPatientInformationContactStateCode_02 = header.PayerState,
                    AdditionalPatientInformationContactPostalZoneorZIPCode_03 = header.PayerZip
                };
            }

            var techContactName = DetermineValue("PER02_PayerTechnicalContact", "TECH SUPPORT", rowData);
            var bizContactName = DetermineValue("PER02_PayerBusinessContact", "", rowData);

            loop.AllPER = new All_PER_835
            {
                PER_PayerTechnicalContactInformation = new List<PER_PayerTechnicalContactInformation>()
            };

            var techContactPhone = DetermineValue("PER04_PayerTechnicalContact", header.PayerCommunicationNumber, rowData);
            if (!string.IsNullOrEmpty(techContactName) || !string.IsNullOrEmpty(techContactPhone))
            {
                var perTech = new PER_PayerTechnicalContactInformation
                {
                    ContactFunctionCode_01 = GetFixedDefault("PER01_PayerTechnicalContactFunctionCode", "BL"),
                    ResponseContactName_02 = techContactName
                };
                if (!string.IsNullOrEmpty(techContactPhone))
                {
                    perTech.CommunicationNumberQualifier_03 = DetermineValue("PER03_PR", "TE", rowData, "TE");
                    perTech.ResponseContactCommunicationNumber_04 = techContactPhone;
                }
                loop.AllPER.PER_PayerTechnicalContactInformation.Add(perTech);
            }

            var bizContactPhone = DetermineValue("PER04_PayerBusinessContact", "", rowData);
            if (!string.IsNullOrEmpty(bizContactName) || !string.IsNullOrEmpty(bizContactPhone))
            {
                loop.AllPER.PER_PayerBusinessContactInformation = new PER_PayerBusinessContactInformation
                {
                    ContactFunctionCode_01 = GetFixedDefault("PER01_PayerBusinessContactFunctionCode", "CX"),
                    ResponseContactName_02 = bizContactName
                };
                if (!string.IsNullOrEmpty(bizContactPhone))
                {
                    loop.AllPER.PER_PayerBusinessContactInformation.CommunicationNumberQualifier_03 = DetermineValue("PER03_PR", "TE", rowData, "TE");
                    loop.AllPER.PER_PayerBusinessContactInformation.ResponseContactCommunicationNumber_04 = bizContactPhone;
                }
            }

            return loop;
        }

        private Loop_1000B_835 BuildLoop1000B(HeaderData header)
        {
            var loop = new Loop_1000B_835();
            var rowData = ToRowData(header);
            var payeeIdValue = DetermineValue("N104_Payee", header.ProviderNpi, rowData);
            loop.N1_PayeeIdentification = new N1_PayeeIdentification
            {
                EntityIdentifierCode_01 = GetFixedDefault("N101_Payee", "PE"),
                PremiumPayerName_02 = DetermineValue("N102_Payee", header.ProviderName, rowData, header.ProviderName)
            };

            if (!string.IsNullOrEmpty(payeeIdValue))
            {
                loop.N1_PayeeIdentification.IdentificationCodeQualifier_03 = DetermineValue("N103_Payee", "XX", rowData, "XX");
                loop.N1_PayeeIdentification.IntermediaryBankIdentifier_04 = payeeIdValue;
            }

            if (!string.IsNullOrEmpty(header.ProviderAddressLine1))
            {
                loop.N3_PayeeAddress = new N3_AdditionalPatientInformationContactAddress { ResponseContactAddressLine_01 = header.ProviderAddressLine1 };
            }

            if (!string.IsNullOrEmpty(header.ProviderCity))
            {
                loop.N4_PayeeCity_State_ZIPCode = new N4_AdditionalPatientInformationContactCity
                {
                    AdditionalPatientInformationContactCityName_01 = header.ProviderCity,
                    AdditionalPatientInformationContactStateCode_02 = header.ProviderState,
                    AdditionalPatientInformationContactPostalZoneorZIPCode_03 = header.ProviderZip
                };
            }

            loop.REF_PayeeAdditionalIdentification = new List<REF_PayeeAdditionalIdentification>();

            // REF - Receiver Identification (Optional, from defaults)
            var receiverIdQual = GetFixedDefault("REF01_ReceiverIdentificationNumber", "");
            if (!string.IsNullOrEmpty(receiverIdQual))
            {
                var receiverId = DetermineValue("REF02_ReceiverID", "", rowData);
                if (!string.IsNullOrEmpty(receiverId))
                {
                    loop.REF_PayeeAdditionalIdentification.Add(new REF_PayeeAdditionalIdentification
                    {
                        ReferenceIdentificationQualifier_01 = receiverIdQual,
                        MemberGrouporPolicyNumber_02 = receiverId
                    });
                }
            }

            // REF - Version Identifier (Optional, from defaults)
            var versionQual = GetFixedDefault("REF01_VersionIdentifier", "");
            if (!string.IsNullOrEmpty(versionQual))
            {
                var version = DetermineValue("REF02_Version", "", rowData);
                if (!string.IsNullOrEmpty(version))
                {
                    loop.REF_PayeeAdditionalIdentification.Add(new REF_PayeeAdditionalIdentification
                    {
                        ReferenceIdentificationQualifier_01 = versionQual,
                        MemberGrouporPolicyNumber_02 = version
                    });
                }
            }

            var refQual = DetermineValue("REF01_PE", "TJ", rowData, "TJ");
            var refCode = DetermineValue("REF02_PE", header.ProviderTaxId, rowData, header.ProviderTaxId);

            if (!string.IsNullOrEmpty(refQual) && !string.IsNullOrEmpty(refCode))
            {
                loop.REF_PayeeAdditionalIdentification.Add(new REF_PayeeAdditionalIdentification
                {
                    ReferenceIdentificationQualifier_01 = refQual,
                    MemberGrouporPolicyNumber_02 = refCode
                });
            }

            return loop;
        }

        private Loop_2000_835 BuildLoop2000(Edi835DataModel data)
        {
            var loop = new Loop_2000_835();
            loop.LX_HeaderNumber = new LX_HeaderNumber { AssignedNumber_01 = "1" };
            loop.Loop2100 = new List<Loop_2100_835>();

            foreach (var claim in data.Claims)
            {
                var loop2100 = new Loop_2100_835();
                var claimRow = ToRowData(claim);

                // Reversal claims: negate CLP/SVC/CAS amounts at EDI output
                decimal signMultiplier = claim.IsReversal ? -1m : 1m;

                loop2100.CLP_ClaimPaymentInformation = new CLP_ClaimPaymentInformation
                {
                    PatientControlNumber_01 = claim.ClaimIdProvider,
                    ClaimStatusCode_02 = claim.IsReversal ? "22" : DetermineValue("CLP02", claim.ClaimStatusCode, claimRow, "1"), // Default to 1 (Processed as Primary)
                    TotalClaimChargeAmount_03 = (claim.ClaimBilledAmount * signMultiplier).ToString("F2"),
                    ClaimPaymentAmount_04 = (claim.ClaimPaidAmount * signMultiplier).ToString("F2"),
                    PatientResponsibilityAmount_05 = claim.PatientResponsibilityAmount.HasValue
                        ? (claim.PatientResponsibilityAmount.Value * signMultiplier).ToString("F2") : null,
                    ClaimFilingIndicatorCode_06 = ResolveClaimFilingIndicator(claim.ClaimType, claimRow),
                    PayerClaimControlNumber_07 = claim.ClaimIdPayer,
                    FacilityTypeCode_08 = DetermineValue("CLP08", "11", claimRow)
                };

                loop2100.AllNM1 = new All_NM1_835
                {
                    NM1_PatientName = new NM1_PatientName_2
                    {
                        EntityIdentifierCode_01 = GetFixedDefault("NM101_PatientNameEntityIdentifier", "QC"),
                        EntityTypeQualifier_02 = GetFixedDefault("NM102_PatientNameEntityTypeQualifier", "1"),
                        ResponseContactLastorOrganizationName_03 = claim.PatientLastName,
                        ResponseContactFirstName_04 = claim.PatientFirstName
                    }
                };

                var patientIdValue = DetermineValue("NM109_Patient", claim.PatientId, claimRow);
                if (!string.IsNullOrEmpty(patientIdValue))
                {
                    loop2100.AllNM1.NM1_PatientName.IdentificationCodeQualifier_08 = DetermineValue("NM108_Patient", "MI", claimRow, "MI");
                    loop2100.AllNM1.NM1_PatientName.ResponseContactIdentifier_09 = patientIdValue;
                }

                string npi = DetermineValue("NM109_Rendering", claim.ProviderRenderingNpi, claimRow);
                if (!string.IsNullOrEmpty(npi) && npi.Length >= 2)
                {
                    loop2100.AllNM1.NM1_ServiceProviderName = new NM1_ServiceProviderName_3
                    {
                        EntityIdentifierCode_01 = GetFixedDefault("NM101_ServiceProviderEntityIdentifier", "82"),
                        EntityTypeQualifier_02 = GetFixedDefault("NM102_Rendering", "1"),
                        ResponseContactLastorOrganizationName_03 = !string.IsNullOrEmpty(claim.ProviderRenderingName) ? claim.ProviderRenderingName : "Rendering Provider",
                        IdentificationCodeQualifier_08 = DetermineValue("NM108_Rendering", "XX", claimRow),
                        ResponseContactIdentifier_09 = npi
                    };
                }

                var claimRowDates = ToRowData(claim); // Reuse or re-create

                loop2100.AllDTM = new All_DTM_835 { DTM_StatementFromorToDate = new List<DTM_StatementFromorToDate>() };
                if (!string.IsNullOrEmpty(claim.ClaimServiceDateFrom))
                    loop2100.AllDTM.DTM_StatementFromorToDate.Add(new DTM_StatementFromorToDate { DateTimeQualifier_01 = GetFixedDefault("DTM01_ClaimStatementFrom", "232"), Date_02 = FormatDate(claim.ClaimServiceDateFrom) });
                if (!string.IsNullOrEmpty(claim.ClaimServiceDateTo))
                    loop2100.AllDTM.DTM_StatementFromorToDate.Add(new DTM_StatementFromorToDate { DateTimeQualifier_01 = GetFixedDefault("DTM01_ClaimStatementTo", "233"), Date_02 = FormatDate(claim.ClaimServiceDateTo) });

                // Claim Received Date (Conditional)
                var receivedDate = DetermineValue("DTM02_ClaimReceived", "", claimRow);
                if (!string.IsNullOrEmpty(receivedDate))
                {
                    loop2100.AllDTM.DTM_StatementFromorToDate.Add(new DTM_StatementFromorToDate { DateTimeQualifier_01 = GetFixedDefault("DTM01_ClaimReceived", "050"), Date_02 = FormatDate(receivedDate) });
                }

                // --- Claim Level Allowed Amount (AMT*AU) ---
                if (claim.ClaimAllowedAmount.HasValue && claim.ClaimAllowedAmount.Value != 0)
                {
                    var amtQual = DetermineValue("AMT01_Allowed", "AU", claimRow, "AU");
                    loop2100.AMT_ClaimSupplementalInformation = new List<AMT_ClaimSupplementalInformation>
                    {
                        new AMT_ClaimSupplementalInformation
                        {
                            AmountQualifierCode_01 = amtQual,
                            TotalClaimChargeAmount_02 = claim.ClaimAllowedAmount.Value.ToString("F2")
                        }
                    };
                }


                // Claim Contact (PER)
                var claimContactName = DetermineValue("PER02_ClaimContact", "", claimRow);
                var claimContactPhone = DetermineValue("PER04_ClaimContact", "", claimRow);
                if (!string.IsNullOrEmpty(claimContactName) || !string.IsNullOrEmpty(claimContactPhone))
                {
                    var perClaim = new PER_ClaimContactInformation
                    {
                        ContactFunctionCode_01 = GetFixedDefault("PER01_ClaimContactContactFunctionCode", "CX"),
                        ResponseContactName_02 = claimContactName
                    };
                    if (!string.IsNullOrEmpty(claimContactPhone))
                    {
                        perClaim.CommunicationNumberQualifier_03 = GetFixedDefault("PER07_ClaimContactCommunicationNumberQualifier", "EX");
                        perClaim.ResponseContactCommunicationNumber_04 = claimContactPhone;
                    }
                    loop2100.PER_ClaimContactInformation = new List<PER_ClaimContactInformation> { perClaim };
                }

                if (claim.ClaimAdjustments.Any())
                    loop2100.CAS_ClaimsAdjustment = BuildCAS(claim.ClaimAdjustments, claim.IsReversal);

                loop2100.Loop2110 = new List<Loop_2110_835>();
                foreach (var line in claim.ServiceLines)
                {
                    var loop2110 = new Loop_2110_835();
                    var lineRow = ToRowData(line);
                    var rawCpt = !string.IsNullOrEmpty(line.CptCode) ? line.CptCode : (!string.IsNullOrEmpty(line.RevenueCode) ? line.RevenueCode : line.NdcCode);
                    string? mod1 = !string.IsNullOrEmpty(line.Modifier1) ? line.Modifier1 : null;
                    string? mod2 = !string.IsNullOrEmpty(line.Modifier2) ? line.Modifier2 : null;

                    // Extract embedded suffix modifiers if missing (e.g., HC.34354.25 or 9921425)
                    if (!string.IsNullOrEmpty(rawCpt))
                    {
                        var codeWithoutPrefix = rawCpt;
                        if (rawCpt.Contains(":") || rawCpt.Contains(">") || rawCpt.Contains("."))
                        {
                            var prefixMatch = System.Text.RegularExpressions.Regex.Match(rawCpt, @"^(?:[a-zA-Z0-9]{2}[:>\.]\s*)+(.*)$");
                            if (prefixMatch.Success && !string.IsNullOrWhiteSpace(prefixMatch.Groups[1].Value))
                            {
                                codeWithoutPrefix = prefixMatch.Groups[1].Value.Trim();
                            }

                            // Notice we added the dot '.' here to extract .25 from "34354.25"
                            var modParts = codeWithoutPrefix.Split(new[] { ':', '>', '.' }, StringSplitOptions.RemoveEmptyEntries);
                            if (modParts.Length > 1)
                            {
                                var extractedMods = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Skip(modParts, 1));
                                foreach (var ex in extractedMods)
                                {
                                    var cleanEx = ex.Trim();
                                    if (string.IsNullOrEmpty(cleanEx)) continue;

                                    if (string.IsNullOrEmpty(mod1))
                                    {
                                        mod1 = cleanEx;
                                    }
                                    else if (mod1 != cleanEx && string.IsNullOrEmpty(mod2))
                                    {
                                        mod2 = cleanEx;
                                    }
                                }
                            }
                        }
                        else
                        {
                            var strippedSpaces = rawCpt.Replace(" ", "");
                            if (strippedSpaces.Length == 7 || strippedSpaces.Length == 9)
                            {
                                if (string.IsNullOrEmpty(mod1) && strippedSpaces.Length >= 7) mod1 = strippedSpaces.Substring(5, 2);
                                if (string.IsNullOrEmpty(mod2) && strippedSpaces.Length == 9) mod2 = strippedSpaces.Substring(7, 2);
                            }
                        }
                    }

                    // Final deduplication/shifting for existing elements
                    if (mod1 == mod2) mod2 = null;

                    loop2110.SVC_ServicePaymentInformation = new SVC_ServicePaymentInformation
                    {
                        CompositeMedicalProcedureIdentifier_01 = new C003_CompositeMedicalProcedureIdentifier_16
                        {
                            ProductorServiceIDQualifier_01 = ResolveSvcQualifier(rawCpt, lineRow),
                            ProcedureCode_02 = DetermineValue("SVC01-02", rawCpt, lineRow),
                            ProcedureModifier_03 = mod1,
                            ProcedureModifier_04 = mod2
                        },
                        LineItemChargeAmount_02 = (line.LineBilledAmount * signMultiplier).ToString("F2"),
                        MonetaryAmount_03 = (line.LinePaidAmount * signMultiplier).ToString("F2"),
                        RevenueCode_04 = !string.IsNullOrEmpty(line.RevenueCode) ? line.RevenueCode : null,
                        Quantity_05 = string.IsNullOrEmpty(line.Units) ? DetermineValue("SVC05", "1", lineRow) : line.Units
                    };

                    loop2110.DTM_ServiceDate = new List<DTM_ServiceDate>();
                    var dtmQual = DetermineValue("DTM01_ServiceDate", string.Empty, lineRow);
                    if (dtmQual == "472" || dtmQual == GetFixedDefault("DTM01_ServiceDate", "472"))
                    {
                        loop2110.DTM_ServiceDate.Add(new DTM_ServiceDate
                        {
                            DateTimeQualifier_01 = GetFixedDefault("DTM01_ServiceDate", "472"),
                            Date_02 = FormatDate(line.LineServiceDateFrom)
                        });
                    }
                    else if (dtmQual == "150" || dtmQual == GetFixedDefault("DTM01_ServiceDateFrom", "150"))
                    {
                        loop2110.DTM_ServiceDate.Add(new DTM_ServiceDate
                        {
                            DateTimeQualifier_01 = GetFixedDefault("DTM01_ServiceDateFrom", "150"),
                            Date_02 = FormatDate(line.LineServiceDateFrom)
                        });
                        loop2110.DTM_ServiceDate.Add(new DTM_ServiceDate
                        {
                            DateTimeQualifier_01 = GetFixedDefault("DTM01_ServiceDateTo", "151"),
                            Date_02 = FormatDate(line.LineServiceDateTo)
                        });
                    }
                    else if (!string.IsNullOrEmpty(line.LineServiceDateFrom)) // Fallback to original logic if rule didn't return specific qualifier
                    {
                        if (line.LineServiceDateFrom == line.LineServiceDateTo || string.IsNullOrEmpty(line.LineServiceDateTo))
                        {
                            loop2110.DTM_ServiceDate.Add(new DTM_ServiceDate { DateTimeQualifier_01 = GetFixedDefault("DTM01_ServiceDate", "472"), Date_02 = FormatDate(line.LineServiceDateFrom) });
                        }
                        else
                        {
                            loop2110.DTM_ServiceDate.Add(new DTM_ServiceDate { DateTimeQualifier_01 = GetFixedDefault("DTM01_ServiceDateFrom", "150"), Date_02 = FormatDate(line.LineServiceDateFrom) });
                            loop2110.DTM_ServiceDate.Add(new DTM_ServiceDate { DateTimeQualifier_01 = GetFixedDefault("DTM01_ServiceDateTo", "151"), Date_02 = FormatDate(line.LineServiceDateTo) });
                        }
                    }

                    // --- Addition: Service Line Allowed Amount (AMT*B6) ---
                    // Map Allowed Amount to AMT segment (Qualifier B6 by default)
                    if (line.LineAllowedAmount.HasValue && line.LineAllowedAmount.Value != 0)
                    {
                        var amtQualifier = DetermineValue("SVC_AMT01", "B6", lineRow, "B6");
                        loop2110.AMT_ServiceSupplementalAmount = new List<AMT_ServiceSupplementalAmount>
                        {
                            new AMT_ServiceSupplementalAmount
                            {
                                AmountQualifierCode_01 = amtQualifier,
                                TotalClaimChargeAmount_02 = line.LineAllowedAmount.Value.ToString("F2")
                            }
                        };
                    }

                    // --- Balancing Fix (Already run in Pipeline before canonical write-back) ---
                    // We just use the adjustments directly since they're already balanced
                    List<AdjustmentData> finalAdjustments = line.Adjustments.ToList();

                    Log.Information("[CAS-DEBUG] ── POST-BALANCE (from Canonical) ── FinalAdjCount={Count}", finalAdjustments.Count);
                    foreach (var adj in finalAdjustments)
                    {
                        Log.Information("[CAS-DEBUG]   ├─ CAGC='{CAGC}', CARC='{CARC}', Amount={Amount}",
                            adj.AdjustmentGroupCode, adj.AdjustmentReasonCode, adj.AdjustmentAmount);
                    }

                    if (finalAdjustments.Any())
                    {
                        loop2110.CAS_ServiceAdjustment = BuildCAS(finalAdjustments, claim.IsReversal);
                    }

                    // Collect remark codes from the Service Line's LineRemarkCodes property
                    var remarkCodes = new List<string>();
                    if (!string.IsNullOrEmpty(line.LineRemarkCodes))
                    {
                        remarkCodes = line.LineRemarkCodes
                            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(r => r.Trim())
                            .Where(r => !string.IsNullOrEmpty(r) && !new[] { "na", "n/a", "null", "none" }.Contains(r.ToLower()))
                            .Distinct()
                            .ToList();
                    }

                    if (remarkCodes.Any())
                    {
                        Log.Information("[CAS-DEBUG] ── LQ SEGMENTS ── Count={Count}, Codes=[{Codes}]", remarkCodes.Count, string.Join(", ", remarkCodes));
                        loop2110.LQ_HealthCareRemarkCodes = remarkCodes.Select(code => new LQ_HealthCareRemarkCodes
                        {
                            CodeListQualifierCode_01 = GetFixedDefault("LQ01_RemarkCodeQualifier", "HE"),
                            FormIdentifier_02 = code
                        }).ToList();
                    }
                    else
                    {
                        Log.Information("[CAS-DEBUG] ── LQ SEGMENTS ── None");
                    }

                    loop2100.Loop2110.Add(loop2110);
                }
                loop.Loop2100.Add(loop2100);
            }
            return loop;
        }

        private List<PLB_ProviderAdjustment> BuildPLB(List<ProviderAdjustmentData> adjustments)
        {
            return adjustments
                .Where(adj => !string.IsNullOrEmpty(adj.PlbReasonCode) && adj.PlbAmount != 0)
                .Select(adj =>
            {
                var adjRow = new Dictionary<string, string> { { "AdjustmentReasonCode", adj.PlbReasonCode } };
                var plb = new PLB_ProviderAdjustment
                {
                    ProviderIdentifier_01 = adj.ProviderIdentifier,
                    FiscalPeriodDate_02 = DetermineValue("PLB02", adj.FiscalPeriodDate, adjRow, FormatDate(adj.FiscalPeriodDate, "yyyy1231")),
                    AdjustmentIdentifier_03 = new C042_AdjustmentIdentifier
                    {
                        AdjustmentReasonCode_01 = DetermineValue("PLB03-01", adj.PlbReasonCode, adjRow, adj.PlbReasonCode),
                    },
                    ProviderAdjustmentAmount_04 = adj.PlbAmount.ToString("F2")
                };

                var adjId = DetermineValue("PLB03-02", "REF", adjRow);
                if (!string.IsNullOrEmpty(adjId))
                {
                    plb.AdjustmentIdentifier_03.ProviderAdjustmentIdentifier_02 = adjId;
                }

                return plb;
            }).ToList();
        }

        private List<CAS_ClaimsAdjustment> BuildCAS(List<AdjustmentData> adjustments, bool isReversal = false)
        {
            decimal casSign = isReversal ? -1m : 1m;
            // Normalize Group Codes based on CARC Master Mapping
            var validGroups = new HashSet<string> { "CO", "CR", "OA", "PI", "PR" };
            var filteredAdjustments = adjustments
                .Where(a => a.AdjustmentAmount != 0 || !string.IsNullOrEmpty(a.AdjustmentReasonCode))
                .ToList();

            foreach (var adj in filteredAdjustments)
            {
                // Relying entirely on DataNormalizationService for correct grouping and mapping
                if (string.IsNullOrEmpty(adj.AdjustmentGroupCode))
                {
                    adj.AdjustmentGroupCode = GetFixedDefault("CAS01_Default", "PR"); // Ultimate fallback
                }
            }

            var result = new List<CAS_ClaimsAdjustment>();
            var grouped = filteredAdjustments.GroupBy(a => a.AdjustmentGroupCode);
            foreach (var group in grouped)
            {
                if (string.IsNullOrEmpty(group.Key)) continue;

                // ── [NEW] Final Deduplication for EDI Emission ──
                // Even if earlier stages missed it, we must ensure unique codes per CAS group here.
                var uniqueItems = group
                    .GroupBy(a => a.AdjustmentReasonCode?.Trim().ToUpperInvariant() ?? "")
                    .Select(g =>
                    {
                        var first = g.First();
                        first.AdjustmentAmount = g.Sum(x => x.AdjustmentAmount); // Sum amounts for the same code
                        return first;
                    })
                    .ToList();

                var items = uniqueItems;
                for (int i = 0; i < items.Count; i += 6)
                {
                    var chunk = items.Skip(i).Take(6).ToList();
                    var cas = new CAS_ClaimsAdjustment { ClaimAdjustmentGroupCode_01 = group.Key };

                    // Ensure reason code is not empty if amount exists
                    var validChunk = chunk.Select(c =>
                    {
                        if (string.IsNullOrEmpty(c.AdjustmentReasonCode) && c.AdjustmentAmount != 0)
                            c.AdjustmentReasonCode = GetFixedDefault("CAS02_Default", "96");
                        return c;
                    }).Where(c => !string.IsNullOrEmpty(c.AdjustmentReasonCode)).ToList();

                    if (validChunk.Count == 0) continue;

                    if (validChunk.Count > 0) { cas.AdjustmentReasonCode_02 = validChunk[0].AdjustmentReasonCode; cas.AdjustmentAmount_03 = (Math.Abs(validChunk[0].AdjustmentAmount) * casSign).ToString("F2"); }
                    if (validChunk.Count > 1) { cas.AdjustmentReasonCode_05 = validChunk[1].AdjustmentReasonCode; cas.AdjustmentAmount_06 = (Math.Abs(validChunk[1].AdjustmentAmount) * casSign).ToString("F2"); }
                    if (validChunk.Count > 2) { cas.AdjustmentReasonCode_08 = validChunk[2].AdjustmentReasonCode; cas.AdjustmentAmount_09 = (Math.Abs(validChunk[2].AdjustmentAmount) * casSign).ToString("F2"); }
                    if (validChunk.Count > 3) { cas.AdjustmentReasonCode_11 = validChunk[3].AdjustmentReasonCode; cas.AdjustmentAmount_12 = (Math.Abs(validChunk[3].AdjustmentAmount) * casSign).ToString("F2"); }
                    if (validChunk.Count > 4) { cas.AdjustmentReasonCode_14 = validChunk[4].AdjustmentReasonCode; cas.AdjustmentAmount_15 = (Math.Abs(validChunk[4].AdjustmentAmount) * casSign).ToString("F2"); }
                    if (validChunk.Count > 5) { cas.AdjustmentReasonCode_17 = validChunk[5].AdjustmentReasonCode; cas.AdjustmentAmount_18 = (Math.Abs(validChunk[5].AdjustmentAmount) * casSign).ToString("F2"); }

                    var casSegParts = new List<string> { group.Key };
                    foreach (var vc in validChunk) casSegParts.Add($"{vc.AdjustmentReasonCode}*{Math.Abs(vc.AdjustmentAmount) * casSign:F2}");
                    Log.Information("[CAS-DEBUG] ── CAS SEGMENT ── {CasParts}", string.Join("*", casSegParts));

                    result.Add(cas);
                }
            }
            return result;
        }

        /// <summary>
        /// Validates and resolves CLP06 (Claim Filing Indicator Code).
        /// If raw value is a valid X12 835 code, use it. Otherwise, fallback to config sheet.
        /// </summary>
        private string ResolveClaimFilingIndicator(string rawClaimType, Dictionary<string, string> rowData)
        {
            var validCLP06Codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "12", "13", "14", "15", "16", "17",
                "AM", "CH", "DS", "HM", "LM", "MA", "MB", "MC", "OF", "TV", "VA", "WC", "ZZ"
            };

            // Priority 1: Raw value from data (if valid)
            if (!string.IsNullOrEmpty(rawClaimType) && validCLP06Codes.Contains(rawClaimType.Trim()))
            {
                return rawClaimType.Trim();
            }

            // Priority 2: DetermineValue (payer-specific mapping from config)
            var determined = DetermineValue("CLP-06", rawClaimType, rowData);
            if (!string.IsNullOrEmpty(determined) && validCLP06Codes.Contains(determined))
            {
                return determined;
            }

            // Priority 3: Fallback sheet default
            var fallback = _mappings.GetDefault("CLP-06");
            if (!string.IsNullOrEmpty(fallback) && validCLP06Codes.Contains(fallback))
            {
                return fallback;
            }

            // Ultimate fallback
            return GetFixedDefault("CLP06_Default", "12");
        }

        /// <summary>
        /// Resolves the qualifier for SVC01-01. Fallback to HC if empty or ZZ.
        /// </summary>
        private string ResolveSvcQualifier(string rawValue, Dictionary<string, string> rowData)
        {
            var value = DetermineValue("SVC01-01", rawValue, rowData);
            if (string.IsNullOrEmpty(value) || value.Equals("ZZ", StringComparison.OrdinalIgnoreCase))
            {
                // Priority: Config sheet default or "HC" fallback
                var fallback = _mappings.GetDefault("SVC01-01");
                return !string.IsNullOrEmpty(fallback) ? fallback : "HC";
            }
            return value;
        }

        private string GetFixedDefault(string codeName, string defaultValue)
        {
            // Priority 1: 835_default_code sheet
            var fixedVal = _mappings.GetFixedDefault(codeName);
            if (!string.IsNullOrEmpty(fixedVal)) return fixedVal;

            // Priority 2: fallback_codes sheet
            var fallbackVal = _mappings.GetDefault(codeName);
            if (!string.IsNullOrEmpty(fallbackVal)) return fallbackVal;

            // Priority 3: Hardcoded developer default
            return defaultValue;
        }

        private string DetermineValue(string targetField, string rawValue, Dictionary<string, string>? rowData = null, string? fallback = null)
        {
            // CRITICAL USER REQUEST: 835_default_code sheet has priority. 
            // If data is there, don't change it based on secondary logic or engine results.
            var systemDefault = _mappings.GetFixedDefault(targetField);
            if (!string.IsNullOrEmpty(systemDefault)) return systemDefault;

            var context = new RuleExecutionContext
            {
                TargetField = targetField,
                RawValue = rawValue ?? string.Empty,
                Mappings = _mappings,
                GenerationContext = _context,
                DataModel = _currentDataModel!,
                CrossWalkService = _crossWalkService,
                RowData = rowData ?? new Dictionary<string, string>()
            };
            var determined = _engine.DetermineValue(targetField, context);
            Log.Debug("[DetermineValue] Field: {TargetField}, Engine Result: '{Result}', RawValue: '{RawValue}'", targetField, determined, context.RawValue);
            if (!string.IsNullOrEmpty(determined) && determined != context.RawValue) return determined;

            // If determined value is same as raw value, it might be a fallback from engine.
            // Check if there is a config default for this field.
            var configDefault = _mappings.GetDefault(targetField);
            if (!string.IsNullOrEmpty(configDefault)) return configDefault;

            return !string.IsNullOrEmpty(determined) ? determined : (fallback ?? string.Empty);
        }

        private Dictionary<string, string> ToRowData(HeaderData h) => new Dictionary<string, string>
        {
            { "PaymentMethod", h.PaymentMethod },
            { "PayerName", h.PayerName },
            { "PayerID", h.PayerId },
            { "PayerCommunicationNumber", h.PayerCommunicationNumber },
            { "ProviderName", h.ProviderName },
            { "ProviderNPI", h.ProviderNpi },
            { "ProviderTaxID", h.ProviderTaxId },
            { "ProviderAddressLine1", h.ProviderAddressLine1 },
            { "ProviderCity", h.ProviderCity },
            { "ProviderState", h.ProviderState },
            { "ProviderZip", h.ProviderZip }
        };
        private Dictionary<string, string> ToRowData(ClaimData c) => new Dictionary<string, string>
        {
            { "ClaimID_Payer", c.ClaimIdPayer },
            { "ClaimTypeCode", c.ClaimType },
            { "ClaimStatusCode", c.ClaimStatusCode },
            { "PatientId", c.PatientId },
            { "SubscriberId", c.SubscriberId },
            { "ProviderRenderingNpi", c.ProviderRenderingNpi }
        };
        private Dictionary<string, string> ToRowData(ServiceLineData s) => new Dictionary<string, string>
        {
            { "CPTCode", s.CptCode },
            { "RevenueCode", s.RevenueCode },
            { "NDCCode", s.NdcCode },
            { "LineServiceDateFrom", s.LineServiceDateFrom },
            { "LineServiceDateTo", s.LineServiceDateTo }
        };
        private Dictionary<string, string> ToRowData(AdjustmentData a) => new Dictionary<string, string> { { "AdjustmentReasonCode", a.AdjustmentReasonCode }, { "AdjustmentGroupCode", a.AdjustmentGroupCode } };

        private string FormatDate(string dateStr, string fallback = "")
        {
            var format = _mappings.GetSetting("DateFormat", "yyyyMMdd");
            if (string.IsNullOrWhiteSpace(dateStr)) return string.IsNullOrEmpty(fallback) ? DateTime.Now.ToString(format) : fallback;

            var clean = dateStr.Replace("-", "").Replace("/", "").Trim();

            // Handle MMDDYYYY (8 digits, starts with 0 or 1, not 20)
            if (clean.Length == 8 && (clean.StartsWith("0") || clean.StartsWith("1")) && !clean.StartsWith("20"))
            {
                try
                {
                    var mm = clean.Substring(0, 2);
                    var dd = clean.Substring(2, 2);
                    var yyyy = clean.Substring(4, 4);
                    return new DateTime(int.Parse(yyyy), int.Parse(mm), int.Parse(dd)).ToString(format);
                }
                catch { return clean; }
            }

            // Handle MMDDYY (6 digits) -> Prepend 20
            if (clean.Length == 6)
            {
                try
                {
                    var mm = clean.Substring(0, 2);
                    var dd = clean.Substring(2, 2);
                    var yy = clean.Substring(4, 2);
                    int year = int.Parse(yy);
                    string century = year > 70 ? "19" : "20";
                    return new DateTime(int.Parse(century + yy), int.Parse(mm), int.Parse(dd)).ToString(format);
                }
                catch { return clean; }
            }

            if (DateTime.TryParse(dateStr, out var d)) return d.ToString(format);
            return clean;
        }

        private int CalculateSegmentCount(TS835 ts)
        {
            return ts.FlattenSegments().Count();
        }
    }
}
