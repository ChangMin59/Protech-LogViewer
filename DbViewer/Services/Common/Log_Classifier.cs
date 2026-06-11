using DbViewer.Models;

namespace DbViewer.Services.Common
{
    public static class LogClassifier
    {
        public static readonly Dictionary<string, HashSet<string>> CategoryTags = new()
        {
            ["fire"] = new HashSet<string>
            {
                "fire_row",
                "fire_text",
                "fire_strong_text",
                "fire_soft_row"
            },
            ["an_fault"] = new HashSet<string>
            {
                "fault_row",
                "fault_text"
            },
            ["alarm"] = new HashSet<string>
            {
                "alarm_row",
                "alarm_text"
            },
            ["line_fault"] = new HashSet<string>
            {
                "line_fault_row",
                "line_fault_text"
            },
            ["receiver_fault"] = new HashSet<string>
            {
                "receiver_fault_row",
                "receiver_fault_text"
            },
            ["panel_fault"] = new HashSet<string>
            {
                "panel_fault_row",
                "panel_fault_text"
            },
            ["relay_fault"] = new HashSet<string>
            {
                "relay_fault_row",
                "relay_fault_text"
            },
            ["recover"] = new HashSet<string>
            {
                "recover_row"
            },
            ["output"] = new HashSet<string>
            {
                "output_on_badge",
                "output_off_badge"
            },
            ["mcc"] = new HashSet<string>
            {
                "mcc_badge"
            },
            ["other"] = new HashSet<string>
            {
                "on_badge",
                "off_badge",
                "key_badge"
            }
        };

        public static string Classify(LogRow row, int index = 0)
        {
            string rowTag = ClassifyRowTag(row, index);
            List<string> categories = GetCategoryKeys(row, rowTag);

            if (categories.Contains("mcc"))
            {
                return "mcc";
            }

            return categories.FirstOrDefault() ?? "other";
        }

        public static string ClassifyRowTag(LogRow row, int index = 0)
        {
            List<string> values = ToDisplayValues(row);
            return ClassifyValues(values, index);
        }

        public static string ClassifyValues(List<string> values, int index = 0)
        {
            string baseTag = index % 2 == 0 ? "even_row" : "odd_row";

            string logType = GetValue(values, 1).Trim();
            string action = GetValue(values, 2).Trim();
            string section = GetValue(values, 3).Trim();
            string contents = GetValue(values, 4).Trim();
            string packet = GetValue(values, 5).Trim();

            string packetTag = GetPacketRowTag(
                packet: packet,
                logType: logType,
                action: action,
                section: section,
                contents: contents
            );

            if (!string.IsNullOrEmpty(packetTag))
            {
                return packetTag;
            }

            if (contents.Contains("시스템을 복구 했습니다."))
            {
                return "recover_row";
            }

            if (string.IsNullOrWhiteSpace(packet))
            {
                string textTag = GetTextRowTag(
                    logType: logType,
                    action: action,
                    section: section,
                    contents: contents
                );

                if (!string.IsNullOrEmpty(textTag))
                {
                    return textTag;
                }
            }

            HashSet<string> excludedTypes = new()
            {
                "출력",
                "제어",
                "음향",
                "KEY",
                "MCC스위치",
                "MCC"
            };

            if (excludedTypes.Contains(logType))
            {
                return baseTag;
            }

            HashSet<string> clearActions = new()
            {
                "소거",
                "복구",
                "해제"
            };

            HashSet<string> fireTypes = new()
            {
                "화재",
                "AN화재",
                "축적",
                "예보"
            };

            HashSet<string> alarmTypes = new()
            {
                "제경보"
            };

            HashSet<string> lineFaultTypes = new()
            {
                "단선"
            };

            HashSet<string> faultTypes = new()
            {
                "AN고장",
                "MCC 통신고장",
                "MCC통신고장"
            };

            if (fireTypes.Contains(logType))
            {
                if (action == "발생")
                {
                    if (logType == "축적")
                    {
                        return "fire_soft_row";
                    }

                    if (logType == "예보")
                    {
                        return "fire_strong_text";
                    }

                    return "fire_row";
                }

                if (clearActions.Contains(action))
                {
                    return "fire_text";
                }
            }

            if (alarmTypes.Contains(logType))
            {
                if (action == "발생")
                {
                    return "alarm_row";
                }

                if (clearActions.Contains(action))
                {
                    return "alarm_text";
                }
            }

            if (lineFaultTypes.Contains(logType))
            {
                if (action == "발생")
                {
                    return "line_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    return "line_fault_text";
                }
            }

            if (faultTypes.Contains(logType))
            {
                if (action == "발생")
                {
                    return "fault_row";
                }

                if (clearActions.Contains(action))
                {
                    return "fault_text";
                }
            }

            if (logType == "중계기고장")
            {
                if (action == "발생")
                {
                    return "relay_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    return "relay_fault_text";
                }
            }

            if (logType == "수신기고장")
            {
                if (action == "발생")
                {
                    return "receiver_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    return "receiver_fault_text";
                }
            }

            if (logType == "중계반고장")
            {
                if (action == "발생")
                {
                    return "panel_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    return "panel_fault_text";
                }
            }

            return baseTag;
        }

        private static string GetTextRowTag(
            string logType,
            string action,
            string section,
            string contents)
        {
            string actionText = action.Trim().ToUpper();
            string text = $"{section} {contents}".ToUpper();

            HashSet<string> clearActions = new()
            {
                "소거",
                "복구",
                "해제"
            };

            if (logType is "화재" or "AN화재" or "축적" or "예보")
            {
                if (action == "발생")
                {
                    if (logType == "축적")
                    {
                        return "fire_soft_row";
                    }

                    if (logType == "예보")
                    {
                        return "fire_strong_text";
                    }

                    return "fire_row";
                }

                if (clearActions.Contains(action))
                {
                    return "fire_text";
                }
            }

            if (logType == "제경보")
            {
                if (action == "발생")
                {
                    return "alarm_row";
                }

                if (clearActions.Contains(action))
                {
                    return "alarm_text";
                }
            }

            if (logType == "단선")
            {
                if (action == "발생")
                {
                    return "line_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    return "line_fault_text";
                }
            }

            if (logType == "중계기고장")
            {
                if (action == "발생")
                {
                    return "relay_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    return "relay_fault_text";
                }
            }

            if (logType == "AN고장")
            {
                if (action == "발생")
                {
                    return "fault_row";
                }

                if (clearActions.Contains(action))
                {
                    return "fault_text";
                }
            }

            if (logType == "수신기고장")
            {
                if (action == "발생")
                {
                    return "receiver_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    return "receiver_fault_text";
                }
            }

            if (logType == "중계반고장")
            {
                if (action == "발생")
                {
                    return "panel_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    return "panel_fault_text";
                }
            }

            if (logType == "출력")
            {
                if (actionText is "ON" or "기동" || text.Contains(" 기동") || text.Contains("ON"))
                {
                    return "output_on_badge";
                }

                if (actionText is "OFF" or "정지" || text.Contains(" 정지") || text.Contains("OFF"))
                {
                    return "output_off_badge";
                }
            }

            if (logType is "MCC" or "MCC스위치" || text.Contains("MCC"))
            {
                return "mcc_badge";
            }

            if (logType == "KEY")
            {
                return GetKeyRowTag(logType, action, contents);
            }

            if (actionText == "ON" || text.Contains(" ON"))
            {
                return "on_badge";
            }

            if (actionText == "OFF" || text.Contains(" OFF"))
            {
                return "off_badge";
            }

            return "";
        }

        private static string GetPacketRowTag(
            string packet,
            string logType,
            string action,
            string section,
            string contents)
        {
            string text = $"{section} {contents}";

            if (PacketStartsWith(packet, new[] { "NI", "NJ", "NK", "NL", "AR" }))
            {
                return "fire_row";
            }

            if (PacketStartsWith(packet, new[] { "Ni", "Nj", "Nk", "Nl", "Ar" }))
            {
                return "fire_text";
            }

            if (PacketStartsWith(packet, new[] { "AQ", "nI", "nJ", "nK", "nL" }))
            {
                return "fire_soft_row";
            }

            if (PacketStartsWith(packet, new[] { "Aq", "ni", "nj", "nk", "nl" }))
            {
                return "fire_text";
            }

            if (PacketStartsWith(packet, new[] { "AS" }))
            {
                return "fire_strong_text";
            }

            if (PacketStartsWith(packet, new[] { "As" }))
            {
                return "fire_text";
            }

            if (PacketStartsWith(packet, new[] { "NM", "NN", "NO", "NP" }))
            {
                return "alarm_row";
            }

            if (PacketStartsWith(packet, new[] { "Nm", "Nn", "No", "Np" }))
            {
                return "alarm_text";
            }

            if (PacketStartsWith(packet, new[] { "ML" }))
            {
                return GetMlRowTag(
                    defaultAlarmTag: "alarm_row",
                    defaultLineTag: "line_fault_row",
                    logType: logType,
                    text: text
                );
            }

            if (PacketStartsWith(packet, new[] { "Ml" }))
            {
                return GetMlRowTag(
                    defaultAlarmTag: "alarm_text",
                    defaultLineTag: "line_fault_text",
                    logType: logType,
                    text: text
                );
            }

            if (PacketStartsWith(packet, new[] { "NQ", "NR", "NS", "NT" }))
            {
                return "line_fault_row";
            }

            if (PacketStartsWith(packet, new[] { "Nq", "Nr", "Ns", "Nt" }))
            {
                return "line_fault_text";
            }

            if (PacketStartsWith(packet, new[] { "Au", "AW" }))
            {
                return "fault_row";
            }

            if (PacketStartsWith(packet, new[] { "CAU", "CBU", "CCU", "CDU", "CEU", "CFU", "CGU" }))
            {
                return "mcc_badge";
            }

            if (PacketStartsWith(packet, new[] { "nE", "nF", "nG", "nH" }))
            {
                return "output_on_badge";
            }

            if (PacketStartsWith(packet, new[] { "ne", "nf", "ng", "nh" }))
            {
                return "output_off_badge";
            }

            if (PacketStartsWith(packet, new[] { "SJ", "SF", "SN", "SL", "SM" }))
            {
                return "on_badge";
            }

            if (PacketStartsWith(packet, new[] { "Sj", "Sf", "Sn", "Sl", "Sm" }))
            {
                return "off_badge";
            }

            if (PacketStartsWith(packet, new[]
            {
                "SK", "SP", "SQ", "SR", "SS", "ST", "SU", "SV", "SW", "SX", "SY",
                "Sk", "Sp", "Sq", "Sr", "Ss", "St", "Su", "Sv", "Sw", "Sx", "Sy",
                "sA", "sB", "sC", "sD", "sE", "sF", "sG", "sH", "sI",
                "sa", "sb", "sc", "sd", "se", "sf", "sg", "sh", "si"
            }))
            {
                return GetKeyRowTag(logType, action, contents);
            }

            if (PacketStartsWith(packet, new[] { "NU" }))
            {
                if (packet.Contains("FEY"))
                {
                    return "receiver_fault_row";
                }

                return "relay_fault_row";
            }

            if (PacketStartsWith(packet, new[] { "OhY", "OHY" }))
            {
                return "panel_fault_row";
            }

            if (PacketStartsWith(packet, new[] { "OAY", "OBY" }))
            {
                string faultTarget = GetReceiverOrPanelFaultTarget(logType, text);

                if (action == "복구")
                {
                    if (faultTarget == "receiver")
                    {
                        return "receiver_fault_text";
                    }

                    if (faultTarget == "panel")
                    {
                        return "panel_fault_text";
                    }

                    return "fault_text";
                }

                if (faultTarget == "receiver")
                {
                    return "receiver_fault_row";
                }

                if (faultTarget == "panel")
                {
                    return "panel_fault_row";
                }

                return "fault_row";
            }

            if (PacketStartsWith(packet, new[] { "NV", "PF", "PD", "PE", "PA", "PB" }))
            {
                return "relay_fault_row";
            }

            if (PacketStartsWith(packet, new[] { "Pf", "Pd", "Pe", "Pa", "Pb", "Pc" }))
            {
                return "relay_fault_text";
            }

            return "";
        }

        private static string GetKeyRowTag(string logType, string action, string contents)
        {
            if (logType != "KEY")
            {
                return "key_badge";
            }

            string actionText = action.Trim().ToUpper();
            string contentsText = contents.Trim().ToUpper();

            if (actionText == "ON")
            {
                return "on_badge";
            }

            if (actionText == "OFF")
            {
                return "off_badge";
            }

            if (contentsText.Contains("ON"))
            {
                return "on_badge";
            }

            if (contentsText.Contains("OFF"))
            {
                return "off_badge";
            }

            return "key_badge";
        }

        private static string GetReceiverOrPanelFaultTarget(string logType, string text)
        {
            if (logType == "수신기고장" || text.Contains("수신기"))
            {
                return "receiver";
            }

            if (logType == "중계반고장" || text.Contains("중계반"))
            {
                return "panel";
            }

            return "";
        }

        private static string GetMlRowTag(
            string defaultAlarmTag,
            string defaultLineTag,
            string logType,
            string text)
        {
            if (logType == "제경보" || text.Contains("제경보"))
            {
                return defaultAlarmTag;
            }

            if (logType == "단선")
            {
                return defaultLineTag;
            }

            if (logType == "MCC" || new[] { "펌프", "P/S", "MCC" }.Any(text.Contains))
            {
                return "mcc_badge";
            }

            return defaultAlarmTag;
        }

        public static List<string> GetBadgeKeys(LogRow row, string rowTag = "")
        {
            List<string> values = ToDisplayValues(row);
            return GetBadgeKeys(values, rowTag);
        }

        public static List<string> GetBadgeKeys(List<string> values, string rowTag = "")
        {
            List<string> badges = new();

            if (HasMccBadge(values, rowTag))
            {
                badges.Add("mcc_badge");
            }

            string stateBadge = GetOnOffBadgeKey(values, rowTag);

            if (!string.IsNullOrEmpty(stateBadge))
            {
                badges.Add(stateBadge);
            }

            if (rowTag == "key_badge")
            {
                badges.Add("key_badge");
            }

            return badges;
        }

        public static bool HasMccBadge(LogRow row, string rowTag = "")
        {
            return HasMccBadge(ToDisplayValues(row), rowTag);
        }

        public static bool HasMccBadge(List<string> values, string rowTag = "")
        {
            string logType = GetValue(values, 1).Trim();
            string section = GetValue(values, 3).Trim();
            string contents = GetValue(values, 4).Trim();
            string packet = GetValue(values, 5).Trim();

            return rowTag == "mcc_badge"
                   || section.Contains("MCC")
                   || logType is "MCC" or "MCC스위치"
                   || contents.Contains("MCC")
                   || PacketStartsWith(packet, new[] { "CAU", "CBU", "CCU", "CDU", "CEU", "CFU", "CGU" });
        }

        private static string GetOnOffBadgeKey(List<string> values, string rowTag = "")
        {
            string action = GetValue(values, 2).Trim().ToUpper();
            string section = GetValue(values, 3).Trim().ToUpper();
            string contents = GetValue(values, 4).Trim().ToUpper();

            string text = $"{section} {contents}";

            if (action is "ON" or "기동")
            {
                return "on_badge";
            }

            if (action is "OFF" or "정지")
            {
                return "off_badge";
            }

            if (string.IsNullOrWhiteSpace(action))
            {
                if (text.Contains("ON") || text.Contains(" 기동"))
                {
                    return "on_badge";
                }

                if (text.Contains("OFF") || text.Contains(" 정지"))
                {
                    return "off_badge";
                }
            }

            if (rowTag is "on_badge" or "output_on_badge")
            {
                return rowTag;
            }

            if (rowTag is "off_badge" or "output_off_badge")
            {
                return rowTag;
            }

            return "";
        }

        public static bool RowMatchesCategory(LogRow row, string category, int index = 0)
        {
            List<string> values = ToDisplayValues(row);
            string tag = ClassifyValues(values, index);

            if (category == "mcc" && HasMccBadge(values, tag))
            {
                return true;
            }

            return CategoryTags.TryGetValue(category, out HashSet<string>? tags)
                   && tags.Contains(tag);
        }

        public static List<string> GetCategoryKeys(LogRow row, string rowTag)
        {
            List<string> values = ToDisplayValues(row);
            List<string> categories = new();

            foreach (KeyValuePair<string, HashSet<string>> pair in CategoryTags)
            {
                if (pair.Value.Contains(rowTag))
                {
                    categories.Add(pair.Key);
                    break;
                }
            }

            if (rowTag != "mcc_badge" && HasMccBadge(values, rowTag))
            {
                categories.Add("mcc");
            }

            if (categories.Count == 0)
            {
                categories.Add("other");
            }

            return categories.Distinct().ToList();
        }

        private static bool PacketStartsWith(string packet, IEnumerable<string> prefixes)
        {
            return prefixes.Any(packet.StartsWith);
        }

        private static List<string> ToDisplayValues(LogRow row)
        {
            return new List<string>
            {
                row.DTime ?? "",
                row.Type ?? "",
                row.Action ?? "",
                row.Section ?? "",
                row.Contents ?? "",
                row.Packet ?? ""
            };
        }

        private static string GetValue(List<string> values, int index)
        {
            if (index < 0 || index >= values.Count)
            {
                return "";
            }

            return values[index] ?? "";
        }
    }
}
