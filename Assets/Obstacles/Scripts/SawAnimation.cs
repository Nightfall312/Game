using UnityEngine;
using DG.Tweening;

namespace Hyper_Casuel_Obstacle
{
    public class SawAnimation : MonoBehaviour
    {
        [SerializeField] private Transform sawMovement;
        [SerializeField] private Transform sawRotation;
        [SerializeField] private float movePosision;
        [SerializeField] private float moveDuration;

        [SerializeField] private float rotationZ;
        [SerializeField] private float rotateDuration;
        private void Start()
        {
            MoveSaw();
            RotateSaw();
        }

        private void MoveSaw()
        {
            sawMovement.DOLocalMoveX(movePosision, moveDuration, false).SetLoops(-1, LoopType.Yoyo)
            .SetEase(Ease.InOutSine);
        }

        private void RotateSaw()
        {
            var rotationZnew = new Vector3(0, 0, rotationZ);
            sawRotation.DOLocalRotate(rotationZnew, rotateDuration, RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
        }
    }
}