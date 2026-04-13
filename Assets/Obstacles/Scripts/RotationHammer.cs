using DG.Tweening;
using UnityEngine;

namespace Hyper_Casuel_Obstacle
{
    public class RotationHammer : MonoBehaviour
    {
        [SerializeField] private Transform hammer;
        [SerializeField] private float rotateDuration;
        [SerializeField] private bool reverse;

        // Values above 1 slow down the obstacle; below 1 speed it up.
        [SerializeField] private float speedMultiplier = 1.4f;

        private void Start()
        {
            StartAnimation();
        }

        private void StartAnimation()
        {
            float duration = rotateDuration * speedMultiplier;
            if (reverse)
            {
                var rotation = new Vector3(0, -360, 0);
                hammer.DOLocalRotate(rotation, duration, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart)
                    .SetEase(Ease.Linear).SetRelative(true);
            }
            else
            {
                var rotation = new Vector3(0, 360, 0);
                hammer.DOLocalRotate(rotation, duration, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart)
                    .SetEase(Ease.Linear).SetRelative(true);
            }
        }
    }
}