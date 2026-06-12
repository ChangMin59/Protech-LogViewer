using DbViewer.Models;

namespace DbViewer.Services.Common
{
    // DB/TXT에서 읽은 실제 로그 한 줄을 화면 색상, 필터 카테고리, 이동모드 기준으로 분류한다.
    // 실제 입력 예: 2026-06-01 17:04:17 / 중계기고장 / 발생 / 02# 01계통 005중계기 / 중계기 통신고장 / 패킷.
    public static class LogClassifier
    {
        // rowTag를 필터/카운트/이동모드 버튼의 카테고리로 묶는 기준표다.
        // 예: relay_fault_row로 분류된 로그는 "중계기고장" 버튼과 이동모드 중계기고장 탐색에 걸린다.
        public static readonly Dictionary<string, HashSet<string>> CategoryTags = new()
        {
            // Type=화재/AN화재/축적/예보 로그는 모두 화재 버튼에서 찾을 수 있게 묶는다.
            ["fire"] = new HashSet<string>
            {
                "fire_row",
                "fire_text",
                "fire_strong_text",
                "fire_soft_row"
            },
            // 예: Type=AN고장, Action=발생, Contents=AN 연기감지기 통신 고장 발생 -> fault_row.
            ["an_fault"] = new HashSet<string>
            {
                "fault_row",
                "fault_text"
            },
            // 예: Type=제경보, Action=발생/복구 -> alarm_row/alarm_text.
            ["alarm"] = new HashSet<string>
            {
                "alarm_row",
                "alarm_text"
            },
            // 예: Type=단선, Action=발생/복구 -> line_fault_row/line_fault_text.
            ["line_fault"] = new HashSet<string>
            {
                "line_fault_row",
                "line_fault_text"
            },
            // 예: Type=수신기고장 또는 Contents에 수신기 고장 문구 -> receiver_fault_*.
            ["receiver_fault"] = new HashSet<string>
            {
                "receiver_fault_row",
                "receiver_fault_text"
            },
            // 예: Type=중계반고장 또는 Contents에 중계반 고장 문구 -> panel_fault_*.
            ["panel_fault"] = new HashSet<string>
            {
                "panel_fault_row",
                "panel_fault_text"
            },
            // 예: Type=중계기고장, Action=발생, Section=02# 01계통 005중계기 -> relay_fault_row.
            ["relay_fault"] = new HashSet<string>
            {
                "relay_fault_row",
                "relay_fault_text"
            },
            // 예: Contents=시스템을 복구 했습니다. -> recover_row.
            ["recover"] = new HashSet<string>
            {
                "recover_row"
            },
            // 예: Type=출력, Action=ON/OFF 또는 Contents에 기동/정지 -> output_*_badge.
            ["output"] = new HashSet<string>
            {
                "output_on_badge",
                "output_off_badge"
            },
            // 예: Type=MCC, Section=02# MCC 스위치 014번 기동 -> mcc_badge.
            ["mcc"] = new HashSet<string>
            {
                "mcc_badge"
            },
            // 예: Type=KEY, Action=ON, Contents=에스컬레이터 ON -> on_badge 또는 key_badge.
            ["other"] = new HashSet<string>
            {
                "on_badge",
                "off_badge",
                "key_badge"
            }
        };

        // 실제 LogRow 한 줄을 대표 카테고리 이름으로 변환한다.
        // 예: Type=중계기고장, Action=발생이면 rowTag=relay_fault_row, 결과 카테고리=relay_fault.
        public static string Classify(LogRow row, int index = 0)
        {
            // 먼저 실제 로그값을 읽어 화면/필터 공통 rowTag를 만든다.
            string rowTag = ClassifyRowTag(row, index);

            // rowTag가 실제 어떤 카테고리 버튼에 속하는지 찾는다.
            List<string> categories = GetCategoryKeys(row, rowTag);

            // MCC는 ON/OFF와 같이 들어와도 사용자는 MCC 버튼에서 찾으므로 대표값을 mcc로 우선한다.
            if (categories.Contains("mcc"))
            {
                return "mcc";
            }

            return categories.FirstOrDefault() ?? "other";
        }

        // LogRow 모델을 실제 화면 컬럼 순서로 바꾼 뒤 rowTag를 판정한다.
        public static string ClassifyRowTag(LogRow row, int index = 0)
        {
            // DB 모델 필드명을 화면 표시 순서인 시간/구분/상태/위치/내용/패킷으로 맞춘다.
            List<string> values = ToDisplayValues(row);

            // 이후부터는 DB에서 왔든 TXT에서 왔든 같은 배열 기준으로 분류한다.
            return ClassifyValues(values, index);
        }

        // 화면에 표시될 실제 값 목록을 기준으로 행 스타일 rowTag를 결정한다.
        public static string ClassifyValues(List<string> values, int index = 0)
        {
            // 분류가 안 된 일반 로그는 index로 짝/홀 줄무늬만 다르게 보여준다.
            string baseTag = index % 2 == 0 ? "even_row" : "odd_row";

            // 실제 컬럼 순서: 0=시간, 1=구분(Type), 2=상태(Action), 3=위치(Section), 4=내용(Contents), 5=패킷(Packet).
            // 예: 2026-06-01 17:04:17 / AN고장 / 발생 / 02# 03계통 001 AN연기감지기 / 테스트 2번수신기 AN 연기감지기 통신 고장 발생 / ...
            string logType = GetValue(values, 1).Trim();
            string action = GetValue(values, 2).Trim();
            string section = GetValue(values, 3).Trim();
            string contents = GetValue(values, 4).Trim();
            string packet = GetValue(values, 5).Trim();

            // Packet 값이 있으면 Type/Contents보다 원본 수신기 신호가 더 정확해서 패킷 prefix부터 본다.
            // 예: Packet이 NU로 시작하면 중계기/수신기 고장 계열을 먼저 판정한다.
            string packetTag = GetPacketRowTag(
                packet: packet,
                logType: logType,
                action: action,
                section: section,
                contents: contents
            );

            if (!string.IsNullOrEmpty(packetTag))
            {
                // 패킷으로 확정된 로그는 텍스트 보정 없이 바로 해당 rowTag를 사용한다.
                return packetTag;
            }

            // 예: Type이 무엇이든 Contents에 "시스템을 복구 했습니다."가 있으면 복구 카테고리로 고정한다.
            if (contents.Contains("시스템을 복구 했습니다."))
            {
                return "recover_row";
            }

            // Packet이 비어 있는 TXT/변환 로그는 Type/Action/Section/Contents 텍스트로 같은 결과가 나오게 보정한다.
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
                    // 예: Packet 없이 Type=중계기고장, Action=발생이면 relay_fault_row로 확정한다.
                    return textTag;
                }
            }

            // 출력/제어/음향/KEY/MCC는 행 전체 색상보다 내용 앞 배지로 보여주는 실제 상태 로그다.
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
                // 예: Type=KEY, Action=ON은 행 색을 바꾸지 않고 ON 배지만 붙인다.
                return baseTag;
            }

            // 실제 로그에서 "소거/복구/해제"는 발생이 끝난 상태라 행 배경 대신 텍스트 강조로 처리한다.
            HashSet<string> clearActions = new()
            {
                "소거",
                "복구",
                "해제"
            };

            // 실제 Type=화재/AN화재/축적/예보는 모두 화재 버튼에서 이동해야 하므로 한 묶음으로 본다.
            HashSet<string> fireTypes = new()
            {
                "화재",
                "AN화재",
                "축적",
                "예보"
            };

            // 실제 Type=제경보는 발생이면 alarm_row, 복구/해제면 alarm_text가 된다.
            HashSet<string> alarmTypes = new()
            {
                "제경보"
            };

            // 실제 Type=단선은 발생이면 line_fault_row, 복구/해제면 line_fault_text가 된다.
            HashSet<string> lineFaultTypes = new()
            {
                "단선"
            };

            // 실제 Type=AN고장/MCC 통신고장은 fault_row/fault_text로 일반 고장 버튼에 묶는다.
            HashSet<string> faultTypes = new()
            {
                "AN고장",
                "MCC 통신고장",
                "MCC통신고장"
            };

            // 여기부터는 실제 Type과 Action 조합으로 화면 색상을 결정한다.
            if (fireTypes.Contains(logType))
            {
                // 예: Type=화재 또는 AN화재, Action=발생 -> fire_row.
                if (action == "발생")
                {
                    // 예: Type=축적, Action=발생 -> fire_soft_row.
                    if (logType == "축적")
                    {
                        return "fire_soft_row";
                    }

                    // 예: Type=예보, Action=발생 -> fire_strong_text.
                    if (logType == "예보")
                    {
                        return "fire_strong_text";
                    }

                    return "fire_row";
                }

                // 예: Type=화재, Action=복구/소거/해제 -> fire_text.
                if (clearActions.Contains(action))
                {
                    return "fire_text";
                }
            }

            if (alarmTypes.Contains(logType))
            {
                // 예: Type=제경보, Action=발생 -> alarm_row.
                if (action == "발생")
                {
                    return "alarm_row";
                }

                // 예: Type=제경보, Action=복구/해제 -> alarm_text.
                if (clearActions.Contains(action))
                {
                    return "alarm_text";
                }
            }

            if (lineFaultTypes.Contains(logType))
            {
                // 예: Type=단선, Action=발생 -> line_fault_row.
                if (action == "발생")
                {
                    return "line_fault_row";
                }

                // 예: Type=단선, Action=복구/해제 -> line_fault_text.
                if (clearActions.Contains(action))
                {
                    return "line_fault_text";
                }
            }

            if (faultTypes.Contains(logType))
            {
                // 예: Type=AN고장, Action=발생, Contents=AN 연기감지기 통신 고장 발생 -> fault_row.
                if (action == "발생")
                {
                    return "fault_row";
                }

                // 예: Type=AN고장, Action=복구 -> fault_text.
                if (clearActions.Contains(action))
                {
                    return "fault_text";
                }
            }

            if (logType == "중계기고장")
            {
                // 예: Type=중계기고장, Action=발생, Section=02# 01계통 005중계기 -> relay_fault_row.
                if (action == "발생")
                {
                    return "relay_fault_row";
                }

                // 예: Type=중계기고장, Action=복구 -> relay_fault_text.
                if (clearActions.Contains(action))
                {
                    return "relay_fault_text";
                }
            }

            if (logType == "수신기고장")
            {
                // 예: Type=수신기고장, Action=발생 -> receiver_fault_row.
                if (action == "발생")
                {
                    return "receiver_fault_row";
                }

                // 예: Type=수신기고장, Action=복구 -> receiver_fault_text.
                if (clearActions.Contains(action))
                {
                    return "receiver_fault_text";
                }
            }

            if (logType == "중계반고장")
            {
                // 예: Type=중계반고장, Action=발생 -> panel_fault_row.
                if (action == "발생")
                {
                    return "panel_fault_row";
                }

                // 예: Type=중계반고장, Action=복구 -> panel_fault_text.
                if (clearActions.Contains(action))
                {
                    return "panel_fault_text";
                }
            }

            // 예: Type=수신기, Action=OFF, Contents=고장음향 스위치 OFF처럼 전용 행 색상이 없으면 기본 줄무늬로 둔다.
            return baseTag;
        }

        // Packet이 비어 있는 로그를 Type/Action/Section/Contents 텍스트만으로 보정 분류한다.
        // 예: TXT 저장 로그처럼 Packet이 없더라도 "중계기고장 / 발생 / 02# 01계통 005중계기"이면 relay_fault_row가 되어야 한다.
        private static string GetTextRowTag(
            string logType,
            string action,
            string section,
            string contents)
        {
            // Action 비교를 쉽게 하려고 ON/OFF 같은 영문 상태를 대문자로 맞춘다.
            string actionText = action.Trim().ToUpper();

            // Section과 Contents를 합쳐 "02# MCC 스위치 014번 기동 지하주차장 환기휀..." 같은 실제 문장으로 검사한다.
            string text = $"{section} {contents}".ToUpper();

            // 실제 로그에서 발생 반대 상태로 쓰이는 값들을 한 번에 처리한다.
            HashSet<string> clearActions = new()
            {
                "소거",
                "복구",
                "해제"
            };

            if (logType is "화재" or "AN화재" or "축적" or "예보")
            {
                // 예: Type=AN화재, Action=발생 -> fire_row.
                if (action == "발생")
                {
                    if (logType == "축적")
                    {
                        // 예: Type=축적, Action=발생 -> fire_soft_row.
                        return "fire_soft_row";
                    }

                    if (logType == "예보")
                    {
                        // 예: Type=예보, Action=발생 -> fire_strong_text.
                        return "fire_strong_text";
                    }

                    return "fire_row";
                }

                if (clearActions.Contains(action))
                {
                    // 예: Type=화재, Action=복구/소거/해제 -> fire_text.
                    return "fire_text";
                }
            }

            if (logType == "제경보")
            {
                // 예: Type=제경보, Action=발생 -> alarm_row.
                if (action == "발생")
                {
                    return "alarm_row";
                }

                if (clearActions.Contains(action))
                {
                    // 예: Type=제경보, Action=복구/해제 -> alarm_text.
                    return "alarm_text";
                }
            }

            if (logType == "단선")
            {
                // 예: Type=단선, Action=발생 -> line_fault_row.
                if (action == "발생")
                {
                    return "line_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    // 예: Type=단선, Action=복구/해제 -> line_fault_text.
                    return "line_fault_text";
                }
            }

            if (logType == "중계기고장")
            {
                // 예: Type=중계기고장, Action=발생, Contents=중계기 통신고장 -> relay_fault_row.
                if (action == "발생")
                {
                    return "relay_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    // 예: Type=중계기고장, Action=복구 -> relay_fault_text.
                    return "relay_fault_text";
                }
            }

            if (logType == "AN고장")
            {
                // 예: Type=AN고장, Action=발생, Contents=AN 연기감지기 통신 고장 발생 -> fault_row.
                if (action == "발생")
                {
                    return "fault_row";
                }

                if (clearActions.Contains(action))
                {
                    // 예: Type=AN고장, Action=복구 -> fault_text.
                    return "fault_text";
                }
            }

            if (logType == "수신기고장")
            {
                // 예: Type=수신기고장, Action=발생 -> receiver_fault_row.
                if (action == "발생")
                {
                    return "receiver_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    // 예: Type=수신기고장, Action=복구 -> receiver_fault_text.
                    return "receiver_fault_text";
                }
            }

            if (logType == "중계반고장")
            {
                // 예: Type=중계반고장, Action=발생 -> panel_fault_row.
                if (action == "발생")
                {
                    return "panel_fault_row";
                }

                if (clearActions.Contains(action))
                {
                    // 예: Type=중계반고장, Action=복구 -> panel_fault_text.
                    return "panel_fault_text";
                }
            }

            if (logType == "출력")
            {
                // 예: Type=출력, Action=ON 또는 Contents=기동상태 -> output_on_badge.
                if (actionText is "ON" or "기동" || text.Contains(" 기동") || text.Contains("ON"))
                {
                    return "output_on_badge";
                }

                // 예: Type=출력, Action=OFF 또는 Contents=정지상태 -> output_off_badge.
                if (actionText is "OFF" or "정지" || text.Contains(" 정지") || text.Contains("OFF"))
                {
                    return "output_off_badge";
                }
            }

            if (logType is "MCC" or "MCC스위치" || text.Contains("MCC"))
            {
                // 예: Type=MCC, Section=02# MCC 스위치 014번 기동 -> mcc_badge.
                return "mcc_badge";
            }

            if (logType == "KEY")
            {
                // 예: Type=KEY, Action=ON, Contents=에스컬레이터 ON -> on_badge.
                return GetKeyRowTag(logType, action, contents);
            }

            if (actionText == "ON" || text.Contains(" ON"))
            {
                // 예: Type=수신기, Action=ON, Contents=자동복구 ON -> on_badge.
                return "on_badge";
            }

            if (actionText == "OFF" || text.Contains(" OFF"))
            {
                // 예: Type=수신기, Action=OFF, Contents=고장음향 스위치 OFF -> off_badge.
                return "off_badge";
            }

            // 예: Type/Action/내용 어디에도 전용 기준이 없으면 ClassifyValues의 기본 줄무늬로 돌린다.
            return "";
        }

        // 실제 장비 Packet prefix를 기준으로 가장 신뢰도 높은 rowTag를 판정한다.
        // 같은 Type 문구라도 Packet이 있으면 Packet 기준을 먼저 적용한다.
        private static string GetPacketRowTag(
            string packet,
            string logType,
            string action,
            string section,
            string contents)
        {
            // OAY/OBY/ML처럼 Packet만으로 부족한 경우를 위해 Section+Contents 문장을 같이 들고 간다.
            string text = $"{section} {contents}";

            // Packet=NI/NJ/NK/NL/AR... -> 실제 화재 발생으로 보고 fire_row.
            if (PacketStartsWith(packet, new[] { "NI", "NJ", "NK", "NL", "AR" }))
            {
                return "fire_row";
            }

            // Packet=Ni/Nj/Nk/Nl/Ar... -> 실제 화재 복구/소거로 보고 fire_text.
            if (PacketStartsWith(packet, new[] { "Ni", "Nj", "Nk", "Nl", "Ar" }))
            {
                return "fire_text";
            }

            // Packet=AQ/nI/nJ/nK/nL... -> 축적/예비 화재 발생으로 보고 fire_soft_row.
            if (PacketStartsWith(packet, new[] { "AQ", "nI", "nJ", "nK", "nL" }))
            {
                return "fire_soft_row";
            }

            // Packet=Aq/ni/nj/nk/nl... -> 축적/예비 화재 복구로 보고 fire_text.
            if (PacketStartsWith(packet, new[] { "Aq", "ni", "nj", "nk", "nl" }))
            {
                return "fire_text";
            }

            // Packet=AS... -> 예보 발생으로 보고 fire_strong_text.
            if (PacketStartsWith(packet, new[] { "AS" }))
            {
                return "fire_strong_text";
            }

            // Packet=As... -> 예보 복구로 보고 fire_text.
            if (PacketStartsWith(packet, new[] { "As" }))
            {
                return "fire_text";
            }

            // Packet=NM/NN/NO/NP... -> 제경보 발생으로 보고 alarm_row.
            if (PacketStartsWith(packet, new[] { "NM", "NN", "NO", "NP" }))
            {
                return "alarm_row";
            }

            // Packet=Nm/Nn/No/Np... -> 제경보 복구로 보고 alarm_text.
            if (PacketStartsWith(packet, new[] { "Nm", "Nn", "No", "Np" }))
            {
                return "alarm_text";
            }

            if (PacketStartsWith(packet, new[] { "ML" }))
            {
                // Packet=ML...은 제경보/단선/MCC로 갈릴 수 있어 Type과 Contents를 추가로 본다.
                return GetMlRowTag(
                    defaultAlarmTag: "alarm_row",
                    defaultLineTag: "line_fault_row",
                    logType: logType,
                    text: text
                );
            }

            if (PacketStartsWith(packet, new[] { "Ml" }))
            {
                // Packet=Ml...은 ML의 복구 계열이라 기본 복구 rowTag를 넘긴다.
                return GetMlRowTag(
                    defaultAlarmTag: "alarm_text",
                    defaultLineTag: "line_fault_text",
                    logType: logType,
                    text: text
                );
            }

            // Packet=NQ/NR/NS/NT... -> 단선 발생으로 보고 line_fault_row.
            if (PacketStartsWith(packet, new[] { "NQ", "NR", "NS", "NT" }))
            {
                return "line_fault_row";
            }

            // Packet=Nq/Nr/Ns/Nt... -> 단선 복구로 보고 line_fault_text.
            if (PacketStartsWith(packet, new[] { "Nq", "Nr", "Ns", "Nt" }))
            {
                return "line_fault_text";
            }

            // Packet=Au/AW... -> AN/MCC 통신고장 같은 일반 고장 발생으로 보고 fault_row.
            if (PacketStartsWith(packet, new[] { "Au", "AW" }))
            {
                return "fault_row";
            }

            // Packet=CAU/CBU/... -> MCC 설비 상태로 보고 mcc_badge.
            if (PacketStartsWith(packet, new[] { "CAU", "CBU", "CCU", "CDU", "CEU", "CFU", "CGU" }))
            {
                return "mcc_badge";
            }

            // Packet=nE/nF/nG/nH... -> 출력 ON 배지.
            if (PacketStartsWith(packet, new[] { "nE", "nF", "nG", "nH" }))
            {
                return "output_on_badge";
            }

            // Packet=ne/nf/ng/nh... -> 출력 OFF 배지.
            if (PacketStartsWith(packet, new[] { "ne", "nf", "ng", "nh" }))
            {
                return "output_off_badge";
            }

            // Packet=SJ/SF/SN/SL/SM... -> 일반 스위치 ON 배지.
            if (PacketStartsWith(packet, new[] { "SJ", "SF", "SN", "SL", "SM" }))
            {
                return "on_badge";
            }

            // Packet=Sj/Sf/Sn/Sl/Sm... -> 일반 스위치 OFF 배지.
            if (PacketStartsWith(packet, new[] { "Sj", "Sf", "Sn", "Sl", "Sm" }))
            {
                return "off_badge";
            }

            // KEY 계열 Packet은 실제 Type=KEY, Action=ON/OFF, Contents 문구를 다시 봐야 배지가 정확하다.
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
                // Packet=NU...FEY는 수신기 고장, NU만 있으면 중계기 고장으로 본다.
                if (packet.Contains("FEY"))
                {
                    return "receiver_fault_row";
                }

                return "relay_fault_row";
            }

            // Packet=OhY/OHY... -> 중계반 고장 발생.
            if (PacketStartsWith(packet, new[] { "OhY", "OHY" }))
            {
                return "panel_fault_row";
            }

            // Packet=OAY/OBY...는 Type/Contents에 수신기/중계반 문구가 있는지에 따라 대상이 달라진다.
            if (PacketStartsWith(packet, new[] { "OAY", "OBY" }))
            {
                string faultTarget = GetReceiverOrPanelFaultTarget(logType, text);

                // 예: Action=복구 + 수신기 문구 -> receiver_fault_text.
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
                    // 예: Action=발생 + 수신기 문구 -> receiver_fault_row.
                    return "receiver_fault_row";
                }

                if (faultTarget == "panel")
                {
                    // 예: Action=발생 + 중계반 문구 -> panel_fault_row.
                    return "panel_fault_row";
                }

                // 대상 문구가 없으면 일반 고장으로 처리한다.
                return "fault_row";
            }

            // Packet=NV/PF/PD/PE/PA/PB... -> 중계기 고장 발생.
            if (PacketStartsWith(packet, new[] { "NV", "PF", "PD", "PE", "PA", "PB" }))
            {
                return "relay_fault_row";
            }

            // Packet=Pf/Pd/Pe/Pa/Pb/Pc... -> 중계기 고장 복구.
            if (PacketStartsWith(packet, new[] { "Pf", "Pd", "Pe", "Pa", "Pb", "Pc" }))
            {
                return "relay_fault_text";
            }

            // 등록되지 않은 Packet은 Type/Action 텍스트 기준으로 다시 분류하게 빈 값을 돌려준다.
            return "";
        }

        // KEY 로그에서 실제 Action/Contents를 보고 ON/OFF/KEY 배지 중 하나를 결정한다.
        // 예: Type=KEY, Action=ON, Contents=에스컬레이터 ON -> on_badge.
        private static string GetKeyRowTag(string logType, string action, string contents)
        {
            // Packet은 KEY 계열인데 Type이 KEY가 아니면 ON/OFF로 단정하지 않고 KEY 배지만 붙인다.
            if (logType != "KEY")
            {
                return "key_badge";
            }

            // 실제 로그는 Action에 ON/OFF가 있거나 Contents 끝에 "--- ON"처럼 들어올 수 있다.
            string actionText = action.Trim().ToUpper();
            string contentsText = contents.Trim().ToUpper();

            if (actionText == "ON")
            {
                // 예: Type=KEY, Action=ON, Contents=피난사다리 ON.
                return "on_badge";
            }

            if (actionText == "OFF")
            {
                // 예: Type=KEY, Action=OFF, Contents=피난사다리 OFF.
                return "off_badge";
            }

            if (contentsText.Contains("ON"))
            {
                // 예: Action이 비어 있어도 Contents=--- ON이면 ON 배지로 본다.
                return "on_badge";
            }

            if (contentsText.Contains("OFF"))
            {
                // 예: Action이 비어 있어도 Contents=--- OFF이면 OFF 배지로 본다.
                return "off_badge";
            }

            // ON/OFF 문구가 없으면 KEY 상태 로그로만 표시한다.
            return "key_badge";
        }

        // OAY/OBY 고장 Packet에서 실제 고장 대상이 수신기인지 중계반인지 판별한다.
        private static string GetReceiverOrPanelFaultTarget(string logType, string text)
        {
            // 예: Type=수신기고장 또는 Contents에 "수신기" 포함 -> receiver.
            if (logType == "수신기고장" || text.Contains("수신기"))
            {
                return "receiver";
            }

            // 예: Type=중계반고장 또는 Contents에 "중계반" 포함 -> panel.
            if (logType == "중계반고장" || text.Contains("중계반"))
            {
                return "panel";
            }

            // 대상 문구가 없으면 fault_row/fault_text로 처리하게 빈 값을 반환한다.
            return "";
        }

        // ML/Ml Packet처럼 실제 의미가 제경보/단선/MCC로 갈리는 로그를 문구로 좁혀 분류한다.
        private static string GetMlRowTag(
            string defaultAlarmTag,
            string defaultLineTag,
            string logType,
            string text)
        {
            // 예: Type=제경보 또는 Contents에 제경보 포함 -> alarm_row/alarm_text.
            if (logType == "제경보" || text.Contains("제경보"))
            {
                return defaultAlarmTag;
            }

            // 예: Type=단선 -> line_fault_row/line_fault_text.
            if (logType == "단선")
            {
                return defaultLineTag;
            }

            // 예: Contents에 펌프/P-S/MCC가 있으면 제경보가 아니라 MCC 설비 상태로 본다.
            if (logType == "MCC" || new[] { "펌프", "P/S", "MCC" }.Any(text.Contains))
            {
                return "mcc_badge";
            }

            // 실제 대상 문구가 애매하면 ML의 기본값인 제경보로 처리한다.
            return defaultAlarmTag;
        }

        // LogRow 기준으로 실제 화면 Contents 앞에 표시할 배지 목록을 만든다.
        // 예: Type=MCC, Section=02# MCC 스위치 014번 기동 -> [mcc_badge, on_badge].
        public static List<string> GetBadgeKeys(LogRow row, string rowTag = "")
        {
            // DB 모델 입력도 시간/구분/상태/위치/내용/패킷 배열로 바꿔 같은 기준을 적용한다.
            List<string> values = ToDisplayValues(row);
            return GetBadgeKeys(values, rowTag);
        }

        // 표시값과 rowTag를 기준으로 실제 화면 Contents 앞에 표시할 배지 목록을 만든다.
        public static List<string> GetBadgeKeys(List<string> values, string rowTag = "")
        {
            // 배지형 로그는 행 전체 색상을 바꾸지 않고 Contents 앞에 작은 상태 표시를 붙인다.
            List<string> badges = new();

            // 예: Type=MCC 또는 Section/Contents에 MCC가 있으면 MCC 배지를 먼저 붙인다.
            if (HasMccBadge(values, rowTag))
            {
                badges.Add("mcc_badge");
            }

            // 예: Action=ON/기동이면 on_badge, Action=OFF/정지이면 off_badge를 찾는다.
            string stateBadge = GetOnOffBadgeKey(values, rowTag);

            if (!string.IsNullOrEmpty(stateBadge))
            {
                badges.Add(stateBadge);
            }

            if (rowTag == "key_badge")
            {
                // 예: KEY 계열이지만 ON/OFF를 확정할 수 없으면 KEY 배지만 추가한다.
                badges.Add("key_badge");
            }

            return badges;
        }

        // LogRow가 실제 화면에서 MCC 배지를 가져야 하는지 확인한다.
        public static bool HasMccBadge(LogRow row, string rowTag = "")
        {
            // 모델 입력도 표시값 배열로 바꿔서 같은 MCC 판정 기준을 사용한다.
            return HasMccBadge(ToDisplayValues(row), rowTag);
        }

        // 표시값 기준으로 실제 MCC 문구/패킷 포함 여부를 확인한다.
        public static bool HasMccBadge(List<string> values, string rowTag = "")
        {
            // 실제 로그는 Type=MCC로 들어오거나 Section/Contents에 MCC 문구만 들어올 수 있다.
            string logType = GetValue(values, 1).Trim();
            string section = GetValue(values, 3).Trim();
            string contents = GetValue(values, 4).Trim();
            string packet = GetValue(values, 5).Trim();

            // 예: Type=MCC, Section=02# MCC 스위치 014번 기동, Contents=지하주차장 환기휀-지하5층 기동상태.
            return rowTag == "mcc_badge"
                   || section.Contains("MCC")
                   || logType is "MCC" or "MCC스위치"
                   || contents.Contains("MCC")
                   || PacketStartsWith(packet, new[] { "CAU", "CBU", "CCU", "CDU", "CEU", "CFU", "CGU" });
        }

        // 실제 Action/Section/Contents/rowTag를 종합해 ON/OFF 계열 배지를 결정한다.
        private static string GetOnOffBadgeKey(List<string> values, string rowTag = "")
        {
            // Action이 비어 있는 MCC/상태 로그도 있어서 Section과 Contents까지 같이 본다.
            string action = GetValue(values, 2).Trim().ToUpper();
            string section = GetValue(values, 3).Trim().ToUpper();
            string contents = GetValue(values, 4).Trim().ToUpper();

            // 예: Section=02# MCC 스위치 014번 기동, Contents=지하주차장 환기휀-지하5층 기동상태.
            string text = $"{section} {contents}";

            // 예: Action=ON 또는 기동 -> on_badge.
            if (action is "ON" or "기동")
            {
                return "on_badge";
            }

            // 예: Action=OFF 또는 정지 -> off_badge.
            if (action is "OFF" or "정지")
            {
                return "off_badge";
            }

            // Action이 비어 있으면 Section/Contents의 "ON/OFF/기동/정지" 문구로 보정한다.
            if (string.IsNullOrWhiteSpace(action))
            {
                if (text.Contains("ON") || text.Contains(" 기동"))
                {
                    // 예: Contents=지하주차장 환기휀-지하5층 기동상태.
                    return "on_badge";
                }

                if (text.Contains("OFF") || text.Contains(" 정지"))
                {
                    // 예: Contents=지하주차장 환기휀-지하4층 정지상태.
                    return "off_badge";
                }
            }

            // 패킷 분류에서 이미 ON 계열 rowTag가 확정된 경우 그대로 배지로 쓴다.
            if (rowTag is "on_badge" or "output_on_badge")
            {
                return rowTag;
            }

            // 패킷 분류에서 이미 OFF 계열 rowTag가 확정된 경우 그대로 배지로 쓴다.
            if (rowTag is "off_badge" or "output_off_badge")
            {
                return rowTag;
            }

            // 실제 값에서 ON/OFF/기동/정지 근거가 없으면 상태 배지를 붙이지 않는다.
            return "";
        }

        // 특정 실제 로그가 사용자가 누른 카테고리 버튼에 속하는지 필터용으로 판정한다.
        // 예: 사용자가 "중계기고장" 버튼을 누르면 category=relay_fault 로그만 true가 된다.
        public static bool RowMatchesCategory(LogRow row, string category, int index = 0)
        {
            // 먼저 실제 로그 한 줄을 rowTag로 분류한다.
            List<string> values = ToDisplayValues(row);
            string tag = ClassifyValues(values, index);

            if (category == "mcc" && HasMccBadge(values, tag))
            {
                // MCC는 Type이 MCC가 아니어도 Section/Contents/Packet에 MCC가 있으면 MCC 필터에 포함한다.
                return true;
            }

            // 일반 카테고리는 rowTag가 CategoryTags 표에 등록되어 있는지 본다.
            return CategoryTags.TryGetValue(category, out HashSet<string>? tags)
                   && tags.Contains(tag);
        }

        // rowTag 하나를 실제 카운트/필터/이동모드용 카테고리 목록으로 바꾼다.
        // 예: rowTag=relay_fault_row -> [relay_fault], rowTag=mcc_badge -> [mcc].
        public static List<string> GetCategoryKeys(LogRow row, string rowTag)
        {
            // MCC 중복 여부를 보려면 실제 Section/Contents/Packet 값도 필요하다.
            List<string> values = ToDisplayValues(row);
            List<string> categories = new();

            foreach (KeyValuePair<string, HashSet<string>> pair in CategoryTags)
            {
                // 먼저 rowTag가 CategoryTags 중 어느 버튼에 속하는지 찾는다.
                if (pair.Value.Contains(rowTag))
                {
                    categories.Add(pair.Key);
                    break;
                }
            }

            if (rowTag != "mcc_badge" && HasMccBadge(values, rowTag))
            {
                // 예: 원래 ON 배지 로그라도 Contents에 MCC가 있으면 MCC 카운트에도 같이 포함한다.
                categories.Add("mcc");
            }

            if (categories.Count == 0)
            {
                // 어떤 버튼 기준에도 없으면 기타 카테고리로 보낸다.
                categories.Add("other");
            }

            // 같은 카테고리가 두 번 들어가면 카운트가 중복되므로 제거한다.
            return categories.Distinct().ToList();
        }

        // 실제 Packet 문자열이 지정 prefix 중 하나로 시작하는지 확인한다.
        private static bool PacketStartsWith(string packet, IEnumerable<string> prefixes)
        {
            // 예: packet="NU..."이고 prefixes에 "NU"가 있으면 true.
            return prefixes.Any(packet.StartsWith);
        }

        // LogRow를 분류 함수가 쓰는 실제 화면 컬럼 순서로 변환한다.
        private static List<string> ToDisplayValues(LogRow row)
        {
            // 예: TXT 저장 형식과 같은 순서로 맞춰 DB/화면/저장 분류 기준을 통일한다.
            return new List<string>
            {
                // 0: 발생 일시. 예: 2026-06-01 17:04:17.
                row.DTime ?? "",
                // 1: 로그 구분. 예: 중계기고장, AN고장, MCC, KEY, 수신기.
                row.Type ?? "",
                // 2: 상태. 예: 발생, 복구, ON, OFF, 기동, 정지.
                row.Action ?? "",
                // 3: 위치/장비. 예: 02# 01계통 005중계기, 02# MCC 스위치 014번 기동.
                row.Section ?? "",
                // 4: 상세 내용. 예: 중계기 통신고장, 지하주차장 환기휀-지하5층 기동상태.
                row.Contents ?? "",
                // 5: 원본 패킷. 예: NU..., CAU..., 비어 있으면 텍스트 기준으로 보정.
                row.Packet ?? ""
            };
        }

        // 표시값 목록에서 실제 index 값이 없거나 null이어도 안전하게 값을 꺼낸다.
        private static string GetValue(List<string> values, int index)
        {
            // 예: TXT에서 Packet 컬럼이 없어서 index=5가 없으면 빈 문자열로 처리한다.
            if (index < 0 || index >= values.Count)
            {
                return "";
            }

            // DB null 값도 빈 문자열로 통일해서 Trim/Contains에서 터지지 않게 한다.
            return values[index] ?? "";
        }
    }
}
