using UnityEngine;

using pbuddy.StringUtility.RuntimeScripts;

namespace JamUp.UnityUtility
{
    public readonly struct ShaderProperty<TData> 
    {
        public int ID { get; }
        public TData Value { get; }

        public ShaderProperty(string name, string toRemove = null)
        {
            ID = Shader.PropertyToID(toRemove is null ? name : name.RemoveSubString(toRemove));
            Value = default;
        }
        
        // will this be deprecated?
        private ShaderProperty(int id, TData value)
        {
            ID = id;
            Value = value;
        }

        // will this be deprecated?
        public ShaderProperty<TData> WithValue(TData value) => new (ID, value);
    }
}