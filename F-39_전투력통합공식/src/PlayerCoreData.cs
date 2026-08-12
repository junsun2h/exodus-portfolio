using Newtonsoft.Json;
using SimpleJSON;
using System.Collections.Generic;


namespace PX
{
    public partial class CoreData_PlayerGrowth : CommonCoreData
    {
        // 잠금.
        public bool IsLock { get; private set; } = false;

        // 레벨, 단계
        public CryptoValueInt Level { get; set; } = new CryptoValueInt();
    }

    public partial class CoreData_PlayerReinforce : CommonCoreData
    {
        // 잠금.
        public bool IsLock { get; private set; } = false;

        // 레벨, 단계
        public CryptoValueInt Level { get; set; } = new CryptoValueInt();
    }

    public partial class CoreData_PlayerAwaken : CommonCoreData
    {
        // 각성자 등급
        public EAwaken AwakenGrade { get; set; } = EAwaken.None;

        // 각성자 모드의 레벨
        [JsonConverter(typeof(DictionaryWithEnumKeyConverter))]
        public Dictionary<EAwakenElement, CryptoValueInt> AwakenModLevels { get; private set; }

        public CoreData_PlayerAwaken()
        {
            AwakenModLevels = new Dictionary<EAwakenElement, CryptoValueInt>();
        }

        public EAwaken GetNextAwakenGrade()
        {
            int currentValue = (int)AwakenGrade;
            int nextValue = int.MaxValue;
            EAwaken nextGrade = EAwaken.awaken_grade_sss;

            foreach (EAwaken grade in System.Enum.GetValues(typeof(EAwaken)))
            {
                int gradeValue = (int)grade;
                if (gradeValue > currentValue && gradeValue < nextValue)
                {
                    nextValue = gradeValue;
                    nextGrade = grade;
                }
            }

            return nextGrade;
        }
    }

    public partial class CoreData_Player : CommonCoreData
    {
        // 레벨
        public CryptoValueInt Level { get; set; } = new CryptoValueInt();

        // 보유 모드와 값
        [JsonConverter(typeof(DictionaryWithEnumKeyConverter))]
        public Dictionary<EMod, ModValue> DefaultMods { get; private set; }

        // 플레이어 : 성장. 각 항목과 레벨(단계)
        [JsonConverter(typeof(DictionaryWithEnumKeyConverter))]
        public Dictionary<EMod, CoreData_PlayerGrowth> Growths { get; private set; }

        // 플레이어 : 강화. 각 항목과 레벨(단계)
        [JsonConverter(typeof(DictionaryWithEnumKeyConverter))]
        public Dictionary<EMod, CoreData_PlayerReinforce> Reinforces { get; private set; }

        // 플레이어 : 각성
        public CoreData_PlayerAwaken Awaken { get; private set; }
        // 전투력
        public CryptoValueInt CombatPower { get; set; } = new CryptoValueInt();

        public CoreData_Player()
        {
            //TestMod = new ModValue();
            DefaultMods = new Dictionary<EMod, ModValue>();
            Growths = new Dictionary<EMod, CoreData_PlayerGrowth>();
            Reinforces = new Dictionary<EMod, CoreData_PlayerReinforce>();
            Awaken = new CoreData_PlayerAwaken();
        }
    }

    public class PlayerData : Item
    {
        public CoreData_Player CoreData { get; private set; } = null;

        /*public double XPforNextLevel { get; private set; }*/

        /*public Dictionary<string, int> Growth { get; private set; }*/

        public PlayerData(string InUUID)
        {
            UUID = InUUID;

            CoreData = new CoreData_Player();
        }

        ~PlayerData()
        {

        }

        public override bool ParseJsonNode(string InDataKey, JSONNode InNode, ECoreDataChangeType InChangeType)
        {
            return CoreData.ParseJsonNode(InDataKey, InNode, InChangeType);
        }

        public override bool ChangedJsonNode(JSONNode InNode, ECoreDataChangeType InChangeType)
        {
            return CoreData.ChangedJsonNode(InNode, InChangeType);
        }
    }
}