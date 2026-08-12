using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PX
{
    public class OnPropertyChangedCallAttribute : PropertyAttribute
    {
        public string methodName;
        public OnPropertyChangedCallAttribute(string methodNameNoArguments) => methodName = methodNameNoArguments;
    }

#if UNITY_EDITOR

    [CustomPropertyDrawer(typeof(OnPropertyChangedCallAttribute))]
    public class OnPropertyChangedCallAttributePropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            //EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(position, property);

            //if (EditorGUI.EndChangeCheck())
            {
                OnPropertyChangedCallAttribute at = attribute as OnPropertyChangedCallAttribute;
                MethodInfo method = property.serializedObject.targetObject.GetType().GetMethods().Where(m => m.Name == at.methodName).First();

                if (method != null && method.GetParameters().Count() == 0)// Only instantiate methods with 0 parameters
                    method.Invoke(property.serializedObject.targetObject, null);
            }
        }
    }

#endif
}