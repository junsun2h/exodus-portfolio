// 사운드 브라우저 — 인덱스 데이터 모델 및 카테고리 자동 분류
// 프로젝트 내 오디오 클립 1건에 대한 메타데이터를 표현한다.

using System;
using System.Collections.Generic;
using System.Text;

namespace PX.SoundBrowser
{
    /// <summary>
    /// 사운드 카테고리 (비트 플래그 — 한 클립이 여러 카테고리에 속할 수 있음)
    /// </summary>
    [Flags]
    public enum SoundCategory
    {
        None = 0,
        Bgm = 1 << 0,
        Ui = 1 << 1,
        Notify = 1 << 2,
        Hit = 1 << 3,
        Slash = 1 << 4,
        Whoosh = 1 << 5,
        Magic = 1 << 6,
        Explosion = 1 << 7,
        Fire = 1 << 8,
        Ice = 1 << 9,
        Lightning = 1 << 10,
        Weapon = 1 << 11,
        Buff = 1 << 12,
        Death = 1 << 13,
        Monster = 1 << 14,
        Voice = 1 << 15,
        Footstep = 1 << 16,
        Ambient = 1 << 17,
        Loot = 1 << 18,
    }

    /// <summary>
    /// 인덱싱된 오디오 클립 1건
    /// </summary>
    [Serializable]
    public class SoundEntry
    {
        /// <summary>에셋 GUID (파일 이동에도 유지되는 안정적 키)</summary>
        public string Guid;

        /// <summary>Assets/ 로 시작하는 에셋 경로</summary>
        public string Path;

        /// <summary>확장자를 뺀 클립 이름</summary>
        public string Name;

        /// <summary>클립이 들어 있는 폴더 경로 (예: "Assets/GameAssets/Sound/SFX/UI"). 폴더 트리 필터의 키</summary>
        public string Folder;

        /// <summary><see cref="SoundCategory"/> 비트마스크</summary>
        public int Categories;

        /// <summary>게임에 편입된 에셋(Assets/GameAssets/ 하위)인지 여부</summary>
        public bool IsGameAsset;

        /// <summary>사용처 분석 결과 — 게임 에셋에서 참조되고 있는지 여부</summary>
        public bool IsReferenced;

        // --- 클립 메타 (인덱스 빌드 시 채워짐) ---

        /// <summary>재생 길이(초)</summary>
        public float Length;

        /// <summary>채널 수 (1=모노, 2=스테레오)</summary>
        public int Channels;

        /// <summary>샘플링 주파수(Hz)</summary>
        public int Frequency;

        /// <summary>채널당 샘플(프레임) 수</summary>
        public int Samples;

        /// <summary>원본 파일 크기(바이트)</summary>
        public long FileSize;

        /// <summary>소문자 확장자 (예: "wav")</summary>
        public string Extension;

        /// <summary>임포터 로드 방식 (예: "DecompressOnLoad")</summary>
        public string LoadType;

        /// <summary>임포터 압축 포맷 (예: "Vorbis")</summary>
        public string Compression;

        /// <summary>임포터의 모노 강제 설정</summary>
        public bool ForceToMono;

        // --- 파형 분석 결과 (별도 배치로 채워짐) ---

        /// <summary>파형 분석이 끝났는지 (실패한 경우도 true — 재시도 루프를 막는다)</summary>
        public bool Analyzed;

        /// <summary>다운샘플된 파형 엔벨로프. 바이트 배열(0~255)을 Base64로 담는다. 실패 시 빈 문자열</summary>
        public string Waveform;

        /// <summary>최대 진폭 (0~1)</summary>
        public float PeakLevel;

        /// <summary>평균 진폭 (0~1). 정확한 RMS가 아니라 클립끼리 음량을 견주기 위한 근사값</summary>
        public float RmsLevel;

        /// <summary>검색용 소문자 문자열 (이름 + 경로). 직렬화 후 재구성됨</summary>
        [NonSerialized] public string SearchKey;

        /// <summary>Assets/ 접두사를 뗀 폴더 경로 (리스트 표시용). 직렬화 후 재구성됨</summary>
        [NonSerialized] public string ShortFolder;

        /// <summary>디코드된 파형 엔벨로프 캐시. 필요할 때 한 번만 만든다</summary>
        [NonSerialized] private byte[] _envelope;

        /// <summary>검색 키와 표시용 폴더명을 재구성한다 (JSON 로드 직후 호출)</summary>
        public void RebuildSearchKey()
        {
            SearchKey = (Name + " " + Path).ToLowerInvariant();
            ShortFolder = !string.IsNullOrEmpty(Folder) && Folder.StartsWith("Assets/", StringComparison.Ordinal)
                ? Folder.Substring(7)
                : Folder;
        }

        /// <summary>파형 엔벨로프(0~255 바이트 배열). 분석 전이거나 실패했으면 null</summary>
        public byte[] GetEnvelope()
        {
            if (_envelope != null) return _envelope;
            if (string.IsNullOrEmpty(Waveform)) return null;

            try
            {
                _envelope = Convert.FromBase64String(Waveform);
            }
            catch (FormatException)
            {
                Waveform = string.Empty;
                return null;
            }
            return _envelope;
        }

        /// <summary>파형 엔벨로프를 설정한다 (Base64 인코딩 후 저장).</summary>
        public void SetEnvelope(byte[] envelope)
        {
            _envelope = envelope;
            Waveform = envelope != null && envelope.Length > 0
                ? Convert.ToBase64String(envelope)
                : string.Empty;
        }

        /// <summary>메타 정보가 채워졌는지 (인덱스 빌드 중 클립 로드에 실패하면 0으로 남는다)</summary>
        public bool HasMeta => Frequency > 0;
    }

    /// <summary>
    /// 이름·경로 키워드로 사운드 카테고리를 추론한다.
    /// </summary>
    public static class SoundCategoryClassifier
    {
        /// <summary>카테고리별 매칭 키워드. 토큰의 접두사로 일치하면 해당 카테고리로 본다.</summary>
        private static readonly (SoundCategory Category, string[] Keywords)[] Rules =
        {
            (SoundCategory.Bgm,       new[] { "bgm", "music", "theme", "song", "ost", "soundtrack" }),
            (SoundCategory.Ui,        new[] { "ui", "button", "click", "popup", "menu", "tab", "toggle", "select", "confirm", "cancel", "close", "scroll", "slider", "hover", "tap", "cursor" }),
            (SoundCategory.Notify,    new[] { "notify", "notice", "alert", "success", "fail", "error", "complete", "levelup", "unlock", "warning", "quest", "achieve", "mission" }),
            (SoundCategory.Hit,       new[] { "hit", "impact", "punch", "smash", "crash", "strike", "bash", "thud", "blunt", "hurt", "damage", "knock" }),
            (SoundCategory.Slash,     new[] { "slash", "sword", "blade", "cut", "claw", "katana", "swipe", "cleave", "stab", "pierce" }),
            (SoundCategory.Whoosh,    new[] { "whoosh", "swoosh", "woosh", "swing", "dash", "rush", "wind", "air" }),
            (SoundCategory.Magic,     new[] { "magic", "spell", "cast", "arcane", "mana", "enchant", "rune", "summon", "portal", "teleport", "warp", "curse", "holy", "dark", "shadow" }),
            (SoundCategory.Explosion, new[] { "explos", "explod", "blast", "boom", "nova", "burst", "detonat", "shockwave", "bomb" }),
            (SoundCategory.Fire,      new[] { "fire", "flame", "burn", "ember", "blaze", "lava", "magma", "inferno", "torch" }),
            (SoundCategory.Ice,       new[] { "ice", "icy", "frost", "freeze", "frozen", "snow", "blizzard", "cryo", "chill" }),
            (SoundCategory.Lightning, new[] { "lightning", "thunder", "electric", "electro", "shock", "spark", "bolt", "volt", "zap", "plasma" }),
            (SoundCategory.Weapon,    new[] { "gun", "shoot", "shot", "arrow", "bow", "reload", "laser", "cannon", "rifle", "pistol", "missile", "throw", "projectile", "bullet" }),
            (SoundCategory.Buff,      new[] { "buff", "heal", "cure", "regen", "restore", "revive", "shield", "barrier", "protect", "guard", "boost", "aura", "charge" }),
            (SoundCategory.Death,     new[] { "death", "die", "dead", "dying", "defeat", "destroy", "break", "shatter", "collapse", "dissolve" }),
            (SoundCategory.Monster,   new[] { "monster", "beast", "dragon", "zombie", "goblin", "orc", "slime", "wolf", "growl", "roar", "creature", "boss", "enemy", "skeleton" }),
            (SoundCategory.Voice,     new[] { "voice", "shout", "scream", "grunt", "yell", "laugh", "breath", "male", "female", "human", "npc", "dialog" }),
            (SoundCategory.Footstep,  new[] { "footstep", "step", "walk", "run", "foot", "jump", "land" }),
            (SoundCategory.Ambient,   new[] { "ambient", "ambience", "atmo", "environment", "forest", "cave", "rain", "water", "river", "ocean", "wave", "bird", "loop" }),
            (SoundCategory.Loot,      new[] { "loot", "coin", "gold", "gem", "pickup", "drop", "chest", "treasure", "item", "equip", "inventory", "craft", "upgrade", "money", "purchase", "reward" }),
        };

        /// <summary>UI 표시용 카테고리 목록 (선언 순서 유지)</summary>
        public static readonly SoundCategory[] AllCategories = BuildAllCategories();

        private static SoundCategory[] BuildAllCategories()
        {
            var list = new List<SoundCategory>(Rules.Length);
            foreach (var rule in Rules)
            {
                list.Add(rule.Category);
            }
            return list.ToArray();
        }

        /// <summary>카테고리의 한글 표시명</summary>
        public static string GetDisplayName(SoundCategory category)
        {
            switch (category)
            {
                case SoundCategory.Bgm: return "음악";
                case SoundCategory.Ui: return "UI";
                case SoundCategory.Notify: return "알림";
                case SoundCategory.Hit: return "타격";
                case SoundCategory.Slash: return "참격";
                case SoundCategory.Whoosh: return "스윙";
                case SoundCategory.Magic: return "마법";
                case SoundCategory.Explosion: return "폭발";
                case SoundCategory.Fire: return "불";
                case SoundCategory.Ice: return "얼음";
                case SoundCategory.Lightning: return "번개";
                case SoundCategory.Weapon: return "무기/발사";
                case SoundCategory.Buff: return "버프/회복";
                case SoundCategory.Death: return "사망/파괴";
                case SoundCategory.Monster: return "몬스터";
                case SoundCategory.Voice: return "음성";
                case SoundCategory.Footstep: return "발소리";
                case SoundCategory.Ambient: return "환경음";
                case SoundCategory.Loot: return "획득/보상";
                default: return category.ToString();
            }
        }

        /// <summary>
        /// 에셋 이름과 경로에서 카테고리 비트마스크를 추론한다.
        /// </summary>
        /// <param name="name">클립 이름</param>
        /// <param name="path">에셋 경로 (폴더명도 분류 힌트로 사용)</param>
        public static int Classify(string name, string path)
        {
            var tokens = new HashSet<string>();
            Tokenize(name, tokens);
            Tokenize(path, tokens);

            SoundCategory result = SoundCategory.None;
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
        /// 예) "SFX_UiButtonClick01" → sfx, ui, button, click
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
