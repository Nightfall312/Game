using UnityEngine;
using DG.Tweening;

namespace Hyper_Casuel_Obstacle
{
    public class GrinderAnimation : MonoBehaviour
    {
        [SerializeField] private Transform grinder;
        [SerializeField] private float rotateDuration;

        // Values above 1 slow down the obstacle; below 1 speed it up.
        [SerializeField] private float speedMultiplier = 1.4f;

        private void Start()
        {
            RotateGrinder();
        }
        private void RotateGrinder()
        {
            var rotationValue = new Vector3(0, 360, 0);
            grinder.DOLocalRotate(rotationValue, rotateDuration * speedMultiplier, RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
        }
    }
}