using DG.Tweening;
using UnityEngine;

namespace Hyper_Casuel_Obstacle
{
    public class RotationWallAnimation : MonoBehaviour
    {
        [SerializeField] private Transform wall;

        [SerializeField] private float duraction;

        private void Start()
        {
            StartAnimation();
        }
        private void StartAnimation()
        {
            var newRotation = new Vector3(0, 360, 0);
            wall.DOLocalRotate(newRotation, duraction, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
        }
    }
}
