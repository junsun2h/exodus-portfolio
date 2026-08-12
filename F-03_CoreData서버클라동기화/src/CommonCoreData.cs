using Newtonsoft.Json;
using SimpleJSON;
using System;
using System.Collections.Generic;

using UnityEngine;

namespace PX
{
    // 프로퍼티를 코드생성 대상에서 제외시키기 위한 어트리뷰트. 
    // JsonParse 에서도 제외된다.
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public class ExcludeCodeGenerateProperty : Attribute
    {
        public ExcludeCodeGenerateProperty() { }
    }

    // 프로퍼티를 JsonParse 대상에서만 제외시키기 위한 어트리뷰트. 
    // 예) 서버에서만 쓰는 프로퍼티
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public class ExcludeCodeGenerateJsonParseProperty : Attribute
    {
        public ExcludeCodeGenerateJsonParseProperty() { }
    }


    // 클래스, 인터페이스, 구조체를 코드생성(CodeGenerator)에서 제외
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, Inherited = false)]
    public class ExcludeCodeGenerateClass : Attribute
    {
        public ExcludeCodeGenerateClass() { }
    }


    [AttributeUsage(AttributeTargets.Property)]
    public class DictionaryEnumKey : Attribute
    {
        public Type keyType { get; set; }

        public DictionaryEnumKey(Type InType)
        {
            keyType = InType;
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    sealed class NameAttribute : Attribute
    {
        public string Name { get; }

        public NameAttribute(string name)
        {
            Name = name;
        }
    }

    public interface IChangeJsonData
    {
        bool FromJson(JSONNode InJson, ECoreDataChangeType InChangeType);
        bool ParseJsonNode(string InDataKey, JSONNode InNode, ECoreDataChangeType InChangeType);
    }

    public interface IJsonData
    {
        bool FromJson(JSONNode InJson);
        bool ParseJsonNode(string InDataKey, JSONNode InNode);
    }

    public abstract class CommonCoreData : IChangeJsonData
    {
        public delegate void CoreDataChangedDelegate(string InDataKey, ECoreDataChangeType InChanged);
        [JsonIgnore] public CoreDataChangedDelegate coreDataChangedDelegate;

        public virtual void InitData() { }

        private bool isInitData;

        public virtual bool FromJson(JSONNode InJson, ECoreDataChangeType InChangeType)
        {
            if (InJson == null)
                return false;

            try
            {
                JSONNode.Enumerator enumerator = InJson.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    KeyValuePair<string, JSONNode> currentData = enumerator.Current;
                    if (ParseJsonNode(currentData.Key, currentData.Value, InChangeType) == false)
                        return false;
                }

                if (isInitData == false)
                {
                    isInitData = true;
                    InitData();
                }
            }
            catch (Exception e)
            {
                Debug.LogError(string.Format("FromJson Failed!! error = {0}", e.ToString()));
                return false;
            }

            return true;
        }

        public virtual bool ParseJsonNode(string InDataKey, JSONNode InNode, ECoreDataChangeType InChangeType) { return false; }

        public bool ChangedJsonNode(JSONNode InNode, ECoreDataChangeType InChangeType)
        {
            bool isChangeExist = false;
            JSONNode.Enumerator enumeratorMain = InNode.GetEnumerator();
            while (enumeratorMain.MoveNext())
            {
                isChangeExist = true;
                if (ParseJsonNode(enumeratorMain.Current.Key, enumeratorMain.Current.Value, InChangeType) == false)
                {
                    Debug.LogError($"Failed, ChangedJsonNode Key = {enumeratorMain.Current.Key}");
                }
            }

            return isChangeExist;
        }
    }

    public partial class CoreData_ModAndValue : CommonCoreData
    {
        public EMod Mod { get; set; }
        public ModValue Value { get; set; }

        public CoreData_ModAndValue()
        {
            Value = new ModValue();
        }
    }
}
