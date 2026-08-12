// 이펙트 브라우저 — 인덱스 데이터 모델 및 카테고리 자동 분류
// 프로젝트 내 파티클 이펙트 프리팹 1건에 대한 메타데이터를 표현한다.

using System;
using System.Collections.Generic;
using System.Text;

namespace PX.EffectBrowser
{
    /// <summary>
    /// 이펙트 카테고리 (비트 플래그 — 한 이펙트가 여러 카테고리에 속할 수 있음)
    /// </summary>
    [Flags]
    public enum EffectCategory
    {
        None = 0,
        Fire = 1 << 0,
        Ice = 1 << 1,
        Lightning = 1 << 2,
        Poison = 1 << 3,
        Dark = 1 << 4,
        Holy = 1 << 5,
        Wind = 1 << 6,
        Water = 1 << 7,
        Hit = 1 << 8,
        Slash = 1 << 9,
        Explosion = 1 << 10,
        Projectile = 1 << 11,
        Beam = 1 << 12,
        Aura = 1 << 13,
        Buff = 1 << 14,
        Portal = 1 << 15,
        Loop = 1 << 16,
        Loot = 1 << 17,
        Death = 1 << 18,
        Ui = 1 << 19,
        Smoke = 1 << 20,
        Heal = 1 << 21,
    }

    /// <summary>
    /// 인덱싱된 이펙트 프리팹 1건
    /// </summary>
    [Serializable]
    public class EffectEntry
    {
        /// <summary>에셋 GUID (파일 이동에도 유지되는 안정적 키. 썸네일 파일명으로도 사용)</summary>
        public string Guid;

        /// <summary>Assets/ 로 시작하는 에셋 경로</summary>
        public string Path;

        /// <summary>확장자를 뺀 프리팹 이름</summary>
        public string Name;

        /// <summary>소속 패키지명 (예: "Polygon Arsenal", "GameAssets")</summary>
        public string Package;

        /// <summary><see cref="EffectCategory"/> 비트마스크</summary>
        public int Categories;

        /// <summary>게임에 편입된 에셋(Assets/GameAssets/ 하위)인지 여부</summary>
        public bool IsGameAsset;

        /// <summary>사용처 분석 결과 — 게임 에셋에서 참조되고 있는지 여부</summary>
        public bool IsReferenced;

        // --- 아래 항목은 썸네일 캡처 단계에서 프리팹을 실제로 로드해야 채워진다 ---

        /// <summary>썸네일이 한 번이라도 캡처되었는지 (캡처 시 부가 정보가 함께 채워짐)</summary>
        public bool Captured;

        /// <summary>포함된 ParticleSystem 개수</summary>
        public int ParticleSystemCount;

        /// <summary>추정 재생 길이(초)</summary>
        public float Duration;

        /// <summary>Animator 포함 여부 (파티클 외 애니메이션 기반 연출)</summary>
        public bool HasAnimator;

        /// <summary>검색용 소문자 문자열 (이름 + 패키지 + 경로). 직렬화 후 재구성됨</summary>
        [NonSerialized] public string SearchKey;

        /// <summary>검색 키를 재구성한다 (JSON 로드 직후 호출)</summary>
        public void RebuildSearchKey()
        {
            SearchKey = (Name + " " + Package + " " + Path).ToLowerInvariant();
        }
    }

    /// <summary>
    /// 이름·경로 키워드로 이펙트 카테고리를 추론한다.
    /// </summary>
    public static class EffectCategoryClassifier
    {
        /// <summary>카테고리별 매칭 키워드. 토큰의 접두사로 일치하면 해당 카테고리로 본다.</summary>
        private static readonly (EffectCategory Category, string[] Keywords)[] Rules =
        {
            (EffectCategory.Fire,       new[] { "fire", "flame", "burn", "ember", "blaze", "magma", "lava", "inferno", "hellfire", "torch", "combust" }),
            (EffectCategory.Ice,        new[] { "ice", "icy", "frost", "freeze", "frozen", "snow", "blizzard", "glacier", "cryo", "chill", "coldsnap" }),
            (EffectCategory.Lightning,  new[] { "lightning", "thunder", "electric", "electro", "shock", "spark", "bolt", "volt", "plasma", "storm", "zap" }),
            (EffectCategory.Poison,     new[] { "poison", "toxic", "venom", "acid", "corros", "plague", "contagion", "nature", "leaf", "vine", "spore" }),
            (EffectCategory.Dark,       new[] { "dark", "shadow", "void", "demon", "evil", "curse", "necro", "soul", "blood", "abyss", "corrupt", "wraith", "ghost" }),
            (EffectCategory.Holy,       new[] { "holy", "divine", "sacred", "bless", "angel", "sun", "radiant", "light" }),
            (EffectCategory.Wind,       new[] { "wind", "air", "tornado", "cyclone", "gust", "whirl", "vortex" }),
            (EffectCategory.Water,      new[] { "water", "aqua", "wave", "splash", "bubble", "rain", "liquid", "ocean" }),
            (EffectCategory.Hit,        new[] { "hit", "impact", "punch", "smash", "crash", "strike", "bash", "collision" }),
            (EffectCategory.Slash,      new[] { "slash", "sword", "blade", "cut", "claw", "katana", "sliced", "swipe", "cleave" }),
            (EffectCategory.Explosion,  new[] { "explos", "explod", "blast", "boom", "nova", "burst", "shockwave", "detonat" }),
            (EffectCategory.Projectile, new[] { "projectile", "missile", "arrow", "bullet", "shot", "orb", "ball", "bomb", "shuriken", "spear", "dart" }),
            (EffectCategory.Beam,       new[] { "beam", "laser", "ray", "breath", "cone", "stream" }),
            (EffectCategory.Aura,       new[] { "aura", "glow", "halo", "rune", "circle", "sigil", "field", "zone", "area", "indicator" }),
            (EffectCategory.Buff,       new[] { "buff", "shield", "barrier", "protect", "guard", "reinforce", "empower", "levelup", "upgrade" }),
            (EffectCategory.Portal,     new[] { "portal", "summon", "teleport", "warp", "gate", "spawn", "appear", "rift" }),
            (EffectCategory.Loop,       new[] { "loop", "ambient", "idle", "campfire", "fog", "mist", "aurora" }),
            (EffectCategory.Loot,       new[] { "loot", "coin", "gold", "gem", "reward", "pickup", "drop", "chest", "lootbox", "treasure" }),
            (EffectCategory.Death,      new[] { "death", "die", "dead", "dissolve", "vanish", "disappear", "destroy", "despawn" }),
            (EffectCategory.Ui,         new[] { "ui", "icon", "hud", "menu", "button", "sprite" }),
            (EffectCategory.Smoke,      new[] { "smoke", "dust", "steam", "cloud", "sand", "debris", "ash" }),
            (EffectCategory.Heal,       new[] { "heal", "cure", "regen", "restore", "revive", "life", "medic" }),
        };

        /// <summary>UI 표시용 카테고리 목록 (선언 순서 유지)</summary>
        public static readonly EffectCategory[] AllCategories = BuildAllCategories();

        private static EffectCategory[] BuildAllCategories()
        {
            var list = new List<EffectCategory>(Rules.Length);
            foreach (var rule in Rules)
            {
                list.Add(rule.Category);
            }
            return list.ToArray();
        }

        /// <summary>카테고리의 한글 표시명</summary>
        public static string GetDisplayName(EffectCategory category)
        {
            switch (category)
            {
                case EffectCategory.Fire: return "불";
                case EffectCategory.Ice: return "얼음";
                case EffectCategory.Lightning: return "번개";
                case EffectCategory.Poison: return "독/자연";
                case EffectCategory.Dark: return "암흑";
                case EffectCategory.Holy: return "신성";
                case EffectCategory.Wind: return "바람";
                case EffectCategory.Water: return "물";
                case EffectCategory.Hit: return "타격";
                case EffectCategory.Slash: return "참격";
                case EffectCategory.Explosion: return "폭발";
                case EffectCategory.Projectile: return "투사체";
                case EffectCategory.Beam: return "빔/브레스";
                case EffectCategory.Aura: return "오라/장판";
                case EffectCategory.Buff: return "버프/보호";
                case EffectCategory.Portal: return "포탈/소환";
                case EffectCategory.Loop: return "루프/환경";
                case EffectCategory.Loot: return "획득/보상";
                case EffectCategory.Death: return "사망/소멸";
                case EffectCategory.Ui: return "UI";
                case EffectCategory.Smoke: return "연기/먼지";
                case EffectCategory.Heal: return "회복";
                default: return category.ToString();
            }
        }

        /// <summary>
        /// 에셋 이름과 경로에서 카테고리 비트마스크를 추론한다.
        /// </summary>
        /// <param name="name">프리팹 이름</param>
        /// <param name="path">에셋 경로 (폴더명도 분류 힌트로 사용)</param>
        public static int Classify(string name, string path)
        {
            var tokens = new HashSet<string>();
            Tokenize(name, tokens);
            Tokenize(path, tokens);

            EffectCategory result = EffectCategory.None;
            foreach (var rule in Rules)
            {
                if (MatchesAny(tokens, rule.Keywords))
                {
                    result |= rule.Category;
                }
            }
            return (int)result;
        }

        private static bool MatchesAny(HashSet<string> tokens, string[] keywords)
        {
            foreach (var token in tokens)
            {
                foreach (var keyword in keywords)
                {
                    // 접두사 일치만 허용 — "slice"가 "ice"로 오분류되는 것을 막는다
                    if (token.StartsWith(keyword, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 문자열을 소문자 토큰으로 분해한다.
        /// 구분자(공백, _, -, /, 숫자 등)와 camelCase 경계에서 나눈다.
        /// 예) "PA_IceExplosion01" → pa, ice, explosion
        /// </summary>
        private static void Tokenize(string source, HashSet<string> output)
        {
            if (string.IsNullOrEmpty(source)) return;

            var buffer = new StringBuilder(32);
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsLetter(c))
                {
                    // 소문자→대문자 전환은 새 단어의 시작으로 본다
                    if (buffer.Length > 0 && char.IsUpper(c) && !char.IsUpper(source[i - 1]))
                    {
                        Flush(buffer, output);
                    }
                    buffer.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    Flush(buffer, output);
                }
            }
            Flush(buffer, output);
        }

        private static void Flush(StringBuilder buffer, HashSet<string> output)
        {
            if (buffer.Length > 0)
            {
                output.Add(buffer.ToString());
                buffer.Clear();
            }
        }
    }
}
