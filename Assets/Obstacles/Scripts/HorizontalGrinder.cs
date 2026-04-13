using UnityEngine;
using DG.Tweening;


namespace Hyper_Casuel_Obstacle
{
    public class HorizontalGrinder : MonoBehaviour
    {
        [SerializeField] private Transform grinder;
        [SerializeField] private bool reverse;
        [SerializeField] private float duraction;
        

        private void Start()
        {
            StartAnimation();
        }
        private void StartAnimation()
        {
            if (reverse)
            {
                var rotation = new Vector3(-360, 0, 0);
                grinder.DOLocalRotate(rotation, duraction, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
            }
            else
            {
                var rotation = new Vector3(360, 0, 0);
                grinder.DOLocalRotate(rotation, duraction, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
            }
        }
    }
}
