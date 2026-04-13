using DG.Tweening;
using UnityEngine;

namespace Hyper_Casuel_Obstacle
{
    public class RotationAnimation : MonoBehaviour
    {
        [SerializeField] private Transform stick;
        [SerializeField] private bool reverse;
        [SerializeField] private float duraction;

        // Values above 1 slow down the obstacle; below 1 speed it up.
        [SerializeField] private float speedMultiplier = 1.4f;

        private void Start()
        {
            StartAnimation();
        }
        private void StartAnimation()
        {
            float duration = duraction * speedMultiplier;
            if (reverse)
            {
                var rotation = new Vector3(0, -360, 0);
                stick.DOLocalRotate(rotation, duration, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
            }
            else
            {
                var rotation = new Vector3(0, 360, 0);
                stick.DOLocalRotate(rotation, duration, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
            }
        }
    }
}