namespace PX
{
    public class MonoContext : SingletonDependency<MonoContext>
    {
        private MonoContext()
        {
        }

        ~MonoContext()
        {
        }

        public override void InitData()
        {
            /**
             * Mono Instance Manager
             */
            GameMonoCoroutineManager.CreateInstance();
            GameNetworkManager.CreateInstance();
            GameMonoTimeManager.CreateInstance();
            GameMonoSceneManager.CreateInstance();
            GameMonoADManager.CreateInstance();
            GameSoundManager.CreateInstance();
            GameVideoManager.CreateInstance();

            InitSingleton();
        }
    }
}
