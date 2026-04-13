using UnityEngine;
using DG.Tweening;

namespace Hyper_Casuel_Obstacle
{
    public class GrinderAnimation : MonoBehaviour
    {
        [SerializeField] private Transform grinder;
        [SerializeField] private float rotateDuration;

        private void Start()
        {
            RotateGrinder();
        }
        private void RotateGrinder()
        {
            var rotationValue = new Vector3(0, 360, 0);
            grinder.DOLocalRotate(rotationValue, rotateDuration, RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
        }
    }
}