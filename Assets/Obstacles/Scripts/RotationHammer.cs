using DG.Tweening;
using UnityEngine;

namespace Hyper_Casuel_Obstacle
{
    public class RotationHammer : MonoBehaviour
    {
        [SerializeField] private Transform hammer;
        [SerializeField] private float rotateDuration;
        [SerializeField] private bool reverse;

        private void Start()
        {
            StartAnimation();
        }

        private void StartAnimation()
        {
            if (reverse)
            {
                var rotation = new Vector3(0, -360, 0);
                hammer.DOLocalRotate(rotation, rotateDuration, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart)
                    .SetEase(Ease.Linear).SetRelative(true);
            }
            else
            {
                var rotation = new Vector3(0, 360, 0);
                hammer.DOLocalRotate(rotation, rotateDuration, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart)
                    .SetEase(Ease.Linear).SetRelative(true);
            }
            
        }
    }
}