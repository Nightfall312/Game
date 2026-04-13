using DG.Tweening;
using UnityEngine;

namespace Hyper_Casuel_Obstacle
{
    public class RotationWallAnimation : MonoBehaviour
    {
        [SerializeField] private Transform wall;
        [SerializeField] private float duraction;

        // Values above 1 slow down the obstacle; below 1 speed it up.
        [SerializeField] private float speedMultiplier = 1.4f;

        private void Start()
        {
            StartAnimation();
        }
        private void StartAnimation()
        {
            var newRotation = new Vector3(0, 360, 0);
            wall.DOLocalRotate(newRotation, duraction * speedMultiplier, RotateMode.FastBeyond360)
                .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
        }
    }
}
