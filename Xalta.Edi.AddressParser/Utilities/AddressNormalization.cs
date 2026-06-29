using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Xalta.Edi.AddressParser.Utilities
{
    public static class AddressNormalization
    {
        private static readonly Dictionary<string, string> Abbreviations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Directions ─────────────────────────────────────────────────────
            { "N",      "NORTH"     },
            { "S",      "SOUTH"     },
            { "E",      "EAST"      },
            { "W",      "WEST"      },
            { "NE",     "NORTHEAST" },
            { "NW",     "NORTHWEST" },
            { "SE",     "SOUTHEAST" },
            { "SW",     "SOUTHWEST" },

            // ── City Word Abbreviations ─────────────────────────────────────────
            { "CTY",    "CITY"      },
            { "CY",     "CITY"      },
            { "VLG",    "VILLAGE"   },
            { "VL",     "VILLAGE"   },
            { "TWP",    "TOWNSHIP"  },
            { "TWN",    "TOWN"      },
            { "BORO",   "BOROUGH"   },
            { "BRO",    "BOROUGH"   },

            // ── Saint / Mount / Fort ────────────────────────────────────────────
            { "ST",     "SAINT"     },
            { "STE",    "SAINTE"    },
            { "MT",     "MOUNT"     },
            { "MTN",    "MOUNTAIN"  },
            { "MNT",    "MOUNT"     },
            { "FT",     "FORT"      },

            // ── Nature / Geography ──────────────────────────────────────────────
            { "LK",     "LAKE"      },
            { "LKS",    "LAKES"     },
            { "RV",     "RIVER"     },
            { "RVR",    "RIVER"     },
            { "CRK",    "CREEK"     },
            { "CK",     "CREEK"     },
            { "BRK",    "BROOK"     },
            { "BCH",    "BEACH"     },
            { "BLF",    "BLUFF"     },
            { "BLFS",   "BLUFFS"    },
            { "CLF",    "CLIFF"     },
            { "CLFS",   "CLIFFS"    },
            { "VLY",    "VALLEY"    },
            { "VL Y",   "VALLEY"    },
            { "MDW",    "MEADOW"    },
            { "MDWS",   "MEADOWS"   },
            { "FLD",    "FIELD"     },
            { "FLDS",   "FIELDS"    },
            { "HBR",    "HARBOR"    },
            { "HVN",    "HAVEN"     },
            { "PT",     "POINT"     },
            { "PTS",    "POINTS"    },
            { "IS",     "ISLAND"    },
            { "ISS",    "ISLANDS"   },
            { "ISL",    "ISLAND"    },
            { "ISLE",   "ISLAND"    },
            { "SPGS",   "SPRINGS"   },
            { "SPG",    "SPRING"    },
            { "SPRG",   "SPRING"    },
            { "RDG",    "RIDGE"     },
            { "RDGS",   "RIDGES"    },
            { "GLN",    "GLEN"      },
            { "GLNS",   "GLENS"     },
            { "HL",     "HILL"      },
            { "HLS",    "HILLS"     },
            { "KNL",    "KNOLL"     },
            { "KNLS",   "KNOLLS"    },
            { "ML",     "MILL"      },
            { "MLS",    "MILLS"     },
            { "PLN",    "PLAIN"     },
            { "PLNS",   "PLAINS"    },
            { "PR",     "PRAIRIE"   },
            { "PRR",    "PRAIRIE"   },
            { "SHR",    "SHORE"     },
            { "SHRS",   "SHORES"    },
            { "RCH",    "RANCH"     },
            { "RNCH",   "RANCH"     },
            { "GRV",    "GROVE"     },
            { "GRVS",   "GROVES"    },
            { "FLS",    "FALLS"     },
            { "FL",     "FALLS"     },
            { "BYU",    "BAYOU"     },
            { "BTM",    "BOTTOM"    },
            { "BTMS",   "BOTTOMS"   },

            // ── Street Suffix Abbreviations ─────────────────────────────────────
            { "AVE",    "AVENUE"    },
            { "AV",     "AVENUE"    },
            { "BLVD",   "BOULEVARD" },
            { "BLV",    "BOULEVARD" },
            { "DR",     "DRIVE"     },
            { "RD",     "ROAD"      },
            { "LN",     "LANE"      },
            { "CT",     "COURT"     },
            { "CIR",    "CIRCLE"    },
            { "PL",     "PLACE"     },
            { "TER",    "TERRACE"   },
            { "TERR",   "TERRACE"   },
            { "HWY",    "HIGHWAY"   },
            { "PKWY",   "PARKWAY"   },
            { "PKY",    "PARKWAY"   },
            { "EXP",    "EXPRESSWAY"},
            { "EXPY",   "EXPRESSWAY"},
            { "FWY",    "FREEWAY"   },
            { "JCT",    "JUNCTION"  },
            { "JCTN",   "JUNCTION"  },
            { "ALY",    "ALLEY"     },
            { "XING",   "CROSSING"  },
            { "CRSG",   "CROSSING"  },
            { "CSWY",   "CAUSEWAY"  },
            { "BYPS",   "BYPASS"    },
            { "BYP",    "BYPASS"    },
            { "TRCE",   "TRACE"     },
            { "TRL",    "TRAIL"     },
            { "TRLR",   "TRAILER"   },
            { "TUNL",   "TUNNEL"    },
            { "VIA",    "VIADUCT"   },
            { "WALK",   "WALKWAY"   },
            { "WAY",    "WAY"       },
            { "SQ",     "SQUARE"    },
            { "PLZ",    "PLAZA"     },

            // ── Unit / Building Types ───────────────────────────────────────────
            { "APT",    "APARTMENT" },
            // { "STE",    "SUITE"     }, // Conflicts with Prefix "SAINTE" used in Cities like Ste Genevieve
            { "RM",     "ROOM"      },
            // { "FL",     "FLOOR"     }, // Conflicts with Suffix "FALLS"
            { "DEPT",   "DEPARTMENT"},
            { "BLDG",   "BUILDING"  },
            { "UNIT",   "UNIT"      },

            // ── Misc ────────────────────────────────────────────────────────────
            { "OLD",    "OLD"       },
            { "NEW",    "NEW"       },
            { "CTR",    "CENTER"    },
            { "CNTR",   "CENTER"    },
            { "INT",    "INTERNATIONAL" },
            { "INTL",   "INTERNATIONAL" },
            { "NATL",   "NATIONAL"  },
            { "MEM",    "MEMORIAL"  },
            { "MEML",   "MEMORIAL"  },
            { "GDN",    "GARDEN"    },
            { "GDNS",   "GARDENS"   },
            { "PRK",    "PARK"      },
            // { "PKWY",   "PARKWAY"   }, // Duplicate of entry in Street Suffix
        };

        public static string NormalizeCity(string city)
        {
            if (string.IsNullOrWhiteSpace(city)) return city;

            var parts = city.Split(new[] { ' ', '.', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var normalizedParts = parts.Select(part =>
            {
                if (Abbreviations.TryGetValue(part, out var full))
                {
                    return full;
                }
                return part;
            });

            return string.Join(" ", normalizedParts).ToUpperInvariant();
        }

        public static string NormalizeForComparison(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;

            // Remove punctuation and multiple spaces for a cleaner comparison
            var cleaned = Regex.Replace(input, @"[^\w\s]", "");
            cleaned = Regex.Replace(cleaned, @"\s+", " ");

            return NormalizeCity(cleaned.Trim());
        }
    }
}
