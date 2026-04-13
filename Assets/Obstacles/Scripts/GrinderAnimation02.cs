using UnityEngine;
using DG.Tweening;

namespace Hyper_Casuel_Obstacle
{
    public class GrinderAnimation02 : MonoBehaviour
    {
        [SerializeField] private Transform grinderMovement;
        [SerializeField] private Transform grinderRotation;
        [SerializeField] private float movePosision;
        [SerializeField] private float moveDuration;
        [SerializeField] private float rotateDuration;

        // Values above 1 slow down the obstacle; below 1 speed it up.
        [SerializeField] private float speedMultiplier = 1.4f;

        private void Start()
        {
            MoveGrinder();
            RotateGrinder();
        }
        private void MoveGrinder()
        {
            grinderMovement.DOLocalMoveX(movePosision, moveDuration * speedMultiplier, false)
            .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
        }

        private void RotateGrinder()
        {
            var rotationNew = new Vector3(0, 360, 0);
            grinderRotation.DOLocalRotate(rotationNew, rotateDuration * speedMultiplier, RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
        }
    }
}
