using UnityEngine;

namespace PX
{
    public abstract class MonoEvent
    {
        public virtual void Awake() { }
        public virtual void OnEnable() { }
        public virtual void Start() { }
        public virtual void PostStart() { }
        public virtual void FixedUpdate() { }
        public virtual void Update() { }
        public virtual void LateUpdate() { }
        public virtual void OnWillRenderObject() { }
        public virtual void OnPreCull() { }
        public virtual void OnBecameVisible() { }
        public virtual void OnBecameInvisible() { }
        public virtual void OnPreRender() { }
        public virtual void OnRenderObject() { }
        public virtual void OnPostRender() { }
        public virtual void OnRenderImage(RenderTexture src, RenderTexture dest) { }
        //public virtual void OnDrawGizmos() { }
        public virtual void OnGUI() { }
        public virtual void OnApplicationPause() { }
        public virtual void OnDisable() { }
        public virtual void OnApplicationQuit() { }
        public virtual void OnDestroy() { }

    }
}
