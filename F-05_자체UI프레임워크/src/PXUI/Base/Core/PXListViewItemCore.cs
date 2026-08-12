namespace PX
{
    public class PXListViewItemCore : BaseUserWidget
    {
        private PXListViewCore infinityScrollView;
        private int index = int.MinValue;
        public int Index
        {
            private set
            {
                index = value;
            }
            get
            {
                return index;
            }
        }
        public PXListViewCore GetInfinityScrollView()
        {
            return infinityScrollView;
        }

        // using for setup data
        public virtual void Reload(PXListViewCore infinity, int _index)
        {
            infinityScrollView = infinity;
            Index = _index;
            //todo
        }

        public virtual void SelfReload()
        {
            if (Index != int.MinValue)
            {
                //todo
            }
        }
    }

}
