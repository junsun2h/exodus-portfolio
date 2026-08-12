namespace PX
{
    /// <summary>
    /// 몬스터를 분해하는 단위. 격자로 자르지 않고 "머리 / 몸 / 팔 / 다리 / 무기" 같은
    /// 해부학적 단위로 나누기 위한 슬롯이다.
    ///
    /// 값이 곧 셰이더 상수 배열의 인덱스이므로 순서를 바꾸면 셰이더도 같이 바꿔야 한다.
    /// </summary>
    public enum MonsterShatterPart
    {
        Head = 0,
        Torso = 1,
        ArmL = 2,
        ArmR = 3,
        HandL = 4,
        HandR = 5,
        LegL = 6,
        LegR = 7,
        FootL = 8,
        FootR = 9,
        Weapon = 10,
        Tail = 11,
        WingL = 12,
        WingR = 13,
        Cloth = 14,
    }

    /// <summary>
    /// 본 이름 → 파트 분류.
    ///
    /// 이 프로젝트의 몬스터는 리그 계열이 네 종류 섞여 있다.
    ///   demonKing_upperArmLeft / cyclops_lowerLegRight   (크리처 접두사 + Left·Right)
    ///   RigRArm1 / RigLArmDigit21 / RigPelvis            (Rig 접두사 + R·L 중위)
    ///   UpperArm_L / Foot_R / Spine01 / Wing01_L         (PascalCase + _L·_R)
    ///   upperarm_l / calf_r / thigh_l / pelvis           (UE 마네킹 소문자 + _l·_r)
    /// 이름 규칙은 제각각이지만 전부 영어 해부학 단어를 쓰기 때문에, 표기를 정규화한 뒤
    /// 키워드로 매칭하면 네 계열을 한 번에 덮을 수 있다.
    ///
    /// 검사 순서가 곧 우선순위다. 단어끼리 부분 문자열로 겹치는 경우가 있어서
    /// (eyeBall 의 "ball", forearm 의 "ear", armor 의 "arm", spear 의 "ear")
    /// 겹치는 쪽을 반드시 먼저 걸러낸다.
    /// </summary>
    public static class MonsterShatterParts
    {
        /// <summary>슬롯 개수. 셰이더 상수 배열 길이(16)보다 작아야 한다.</summary>
        public const int Count = 15;

        /// <summary>파트 병합 단계의 최대치. 여기까지 올리면 몸통(+무기)만 남는다.</summary>
        public const int MaxMergeLevel = 5;

        private enum Side
        {
            None,
            Left,
            Right,
        }

        //무기 — 본으로 들어 있는 경우(demonKing_sword, Dagger, Sword)와
        //별도 MeshRenderer 로 붙어 있는 경우(DemonKingBlade, orc_bow)가 둘 다 있다
        private static readonly string[] WeaponKeys =
        {
            "sword", "blade", "dagger", "knife", "axe", "bow", "arrow", "quiver",
            "staff", "wand", "spear", "lance", "hammer", "mace", "club", "scythe",
            "shield", "gun", "weapon", "torch",
        };

        private static readonly string[] HeadKeys =
        {
            "head", "neck", "skull", "jaw", "eye", "lid", "ear", "nose", "mouth",
            "tooth", "teeth", "tongue", "pupil", "face", "horn", "crown", "beak",
            "antler", "antenna", "mane", "helmet",
        };

        private static readonly string[] HandKeys =
        {
            "hand", "palm", "finger", "thumb", "index", "middle", "pinky", "ring", "digit",
        };

        private static readonly string[] ArmKeys =
        {
            "arm", "shoulder", "clavicle", "elbow", "wrist",
        };

        private static readonly string[] FootKeys =
        {
            "foot", "feet", "toe", "ball", "ankle", "heel",
        };

        private static readonly string[] LegKeys =
        {
            "leg", "thigh", "calf", "shin", "knee",
        };

        //천/장식 — 망토, 소매, 머리카락처럼 몸에 딸려 흔들리는 것들
        private static readonly string[] ClothKeys =
        {
            "cape", "cloak", "sleeve", "skirt", "robe", "hair", "cloth",
            "scarf", "ribbon", "tassel", "armor", "belt", "chain",
        };

        /// <summary>본 이름 하나를 파트로 분류한다. 짚이는 게 없으면 몸통으로 본다.</summary>
        public static MonsterShatterPart Classify(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
                return MonsterShatterPart.Torso;

            Side side = DetectSide(boneName);
            string n = Normalize(boneName);

            //1) 무기가 최우선. "spear" 안의 ear 가 머리로, "bow" 가 다른 데로 새지 않게 먼저 뺀다
            if (ContainsAny(n, WeaponKeys))
                return MonsterShatterPart.Weapon;

            //2) forearm 은 머리 키워드 "ear" 와 겹친다
            if (n.Contains("forearm"))
                return Pick(side, MonsterShatterPart.ArmL, MonsterShatterPart.ArmR);

            //3) 머리. eyeBall 이 발("ball")로 가지 않도록 발보다 먼저 본다
            if (ContainsAny(n, HeadKeys))
                return MonsterShatterPart.Head;

            if (n.Contains("tail"))
                return MonsterShatterPart.Tail;

            if (n.Contains("wing"))
                return Pick(side, MonsterShatterPart.WingL, MonsterShatterPart.WingR);

            //4) 손가락은 팔보다 먼저 (RigLArmDigit21 처럼 이름에 arm 이 같이 들어 있다)
            if (ContainsAny(n, HandKeys))
                return Pick(side, MonsterShatterPart.HandL, MonsterShatterPart.HandR);

            //5) armor 안의 "arm" 이 팔로 새지 않게 천을 먼저 본다
            if (ContainsAny(n, ClothKeys))
                return MonsterShatterPart.Cloth;

            if (ContainsAny(n, ArmKeys))
                return Pick(side, MonsterShatterPart.ArmL, MonsterShatterPart.ArmR);

            if (ContainsAny(n, FootKeys))
                return Pick(side, MonsterShatterPart.FootL, MonsterShatterPart.FootR);

            if (ContainsAny(n, LegKeys))
                return Pick(side, MonsterShatterPart.LegL, MonsterShatterPart.LegR);

            //spine, pelvis, ribcage, body, root, belly, back… 은 전부 여기로 떨어진다.
            //모르는 이름을 몸통으로 흡수시키면 최악의 경우에도 유령 조각이 생기지 않는다
            return MonsterShatterPart.Torso;
        }

        /// <summary>
        /// 파트를 더 굵은 단위로 합친다. 0단계는 아무것도 합치지 않는다.
        ///
        /// 2등신처럼 몸이 짧은 몬스터를 해부학 단위 그대로 가르면 조각이 잘아서 지나치게 분절돼 보인다.
        /// 그런 몬스터만 개별 설정으로 단계를 올려 조각을 굵게 만든다.
        ///
        ///   1 : 손→팔, 발→다리
        ///   2 : + 천·날개·꼬리→몸통
        ///   3 : + 팔→몸통
        ///   4 : + 다리→몸통
        ///   5 : + 머리→몸통
        ///
        /// 좌우를 합치지 않는 것이 핵심이다. 왼팔과 오른팔을 한 슬롯에 넣으면 둘이 하나의 강체가 되어
        /// 몸통을 사이에 둔 채 붙어 날아간다. 반드시 맞닿은 파트끼리 안쪽으로만 흡수시킨다.
        ///
        /// 무기는 어느 단계에서도 합치지 않는다. 본체와 따로 날아가는 게 가장 눈에 띄는 조각이고,
        /// 무기 MeshRenderer 가 런타임에 이 슬롯을 강제로 쓰기 때문에 합치면 몸통과 한 몸이 된다.
        /// </summary>
        public static MonsterShatterPart Merge(MonsterShatterPart part, int level)
        {
            if (level <= 0)
                return part;

            switch (part)
            {
                case MonsterShatterPart.HandL: part = MonsterShatterPart.ArmL; break;
                case MonsterShatterPart.HandR: part = MonsterShatterPart.ArmR; break;
                case MonsterShatterPart.FootL: part = MonsterShatterPart.LegL; break;
                case MonsterShatterPart.FootR: part = MonsterShatterPart.LegR; break;
            }

            if (level >= 2 && (part == MonsterShatterPart.Cloth || part == MonsterShatterPart.Tail
                               || part == MonsterShatterPart.WingL || part == MonsterShatterPart.WingR))
                part = MonsterShatterPart.Torso;

            if (level >= 3 && (part == MonsterShatterPart.ArmL || part == MonsterShatterPart.ArmR))
                part = MonsterShatterPart.Torso;

            if (level >= 4 && (part == MonsterShatterPart.LegL || part == MonsterShatterPart.LegR))
                part = MonsterShatterPart.Torso;

            if (level >= 5 && part == MonsterShatterPart.Head)
                part = MonsterShatterPart.Torso;

            return part;
        }

        public static string KoreanName(MonsterShatterPart part)
        {
            switch (part)
            {
                case MonsterShatterPart.Head:   return "머리";
                case MonsterShatterPart.Torso:  return "몸통";
                case MonsterShatterPart.ArmL:   return "왼팔";
                case MonsterShatterPart.ArmR:   return "오른팔";
                case MonsterShatterPart.HandL:  return "왼손";
                case MonsterShatterPart.HandR:  return "오른손";
                case MonsterShatterPart.LegL:   return "왼다리";
                case MonsterShatterPart.LegR:   return "오른다리";
                case MonsterShatterPart.FootL:  return "왼발";
                case MonsterShatterPart.FootR:  return "오른발";
                case MonsterShatterPart.Weapon: return "무기";
                case MonsterShatterPart.Tail:   return "꼬리";
                case MonsterShatterPart.WingL:  return "왼날개";
                case MonsterShatterPart.WingR:  return "오른날개";
                case MonsterShatterPart.Cloth:  return "천";
                default: return part.ToString();
            }
        }

        private static MonsterShatterPart Pick(Side side, MonsterShatterPart left, MonsterShatterPart right)
        {
            //좌우 표기가 없는 리그면 양쪽이 한 조각으로 합쳐진다. 이름만 왼쪽일 뿐 동작은 정상이다
            return side == Side.Right ? right : left;
        }

        /// <summary>소문자로 낮추고 구분자를 없앤다. 숫자는 남겨도 키워드 매칭에 지장이 없다.</summary>
        private static string Normalize(string raw)
        {
            var buffer = new System.Text.StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '_' || c == '-' || c == '.' || c == ' ' || c == ':')
                    continue;

                buffer.Append(char.ToLowerInvariant(c));
            }

            return buffer.ToString();
        }

        /// <summary>
        /// 좌우를 판정한다. 소문자로 낮춘 문자열만 보면 "RigTail1" 의 l 을 왼쪽으로 오인하므로,
        /// 대소문자가 필요한 규칙(3·4번)은 원본 문자열을 그대로 본다.
        /// </summary>
        private static Side DetectSide(string raw)
        {
            string lower = raw.ToLowerInvariant();

            //1) left / right 단어가 그대로 들어 있는 경우 (upperArmLeft, shoulder_pad_right)
            int leftAt = lower.IndexOf("left", System.StringComparison.Ordinal);
            int rightAt = lower.IndexOf("right", System.StringComparison.Ordinal);
            if (leftAt >= 0 && (rightAt < 0 || leftAt < rightAt))
                return Side.Left;
            if (rightAt >= 0)
                return Side.Right;

            //2) 구분자 뒤에 글자 하나만 남는 경우 (upperarm_l, Hand_L, Ear01_R)
            int last = raw.Length - 1;
            if (last >= 1)
            {
                char sep = raw[last - 1];
                if (sep == '_' || sep == '-' || sep == '.' || sep == ' ')
                {
                    char s = char.ToLowerInvariant(raw[last]);
                    if (s == 'l') return Side.Left;
                    if (s == 'r') return Side.Right;
                }
            }

            //3) 대문자 R/L 바로 뒤에 숫자가 붙어 끝나는 경우 (RigCapeR1, RigCapeL1).
            //   "RigTail1" 은 소문자 l 이므로 여기 걸리지 않는다
            int end = raw.Length;
            while (end > 0 && char.IsDigit(raw[end - 1]))
                end--;

            if (end < raw.Length && end > 0)
            {
                char s = raw[end - 1];
                if (s == 'L') return Side.Left;
                if (s == 'R') return Side.Right;
            }

            //4) Rig 접두사 바로 뒤의 대문자 R/L (RigRArm1, RigLArmDigit21)
            if (raw.Length >= 5 && raw[0] == 'R' && raw[1] == 'i' && raw[2] == 'g' && char.IsUpper(raw[4]))
            {
                char s = raw[3];
                if (s == 'L') return Side.Left;
                if (s == 'R') return Side.Right;
            }

            return Side.None;
        }

        private static bool ContainsAny(string normalized, string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (normalized.Contains(keys[i]))
                    return true;
            }

            return false;
        }
    }
}
