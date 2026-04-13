using DG.Tweening;
using UnityEngine;

namespace Hyper_Casuel_Obstacle
{
    public class WallMoveRotateAnimation : MonoBehaviour
    {
        [SerializeField] private Transform moveTransform;
        [SerializeField] private Transform rotateTransform;

        [SerializeField] private float moveDuraction;
        [SerializeField] private float moveValue;
        [SerializeField] private float rotationDuraction;


        private void Start()
        {
            MoveWall();
            // RotateWall();
        }

        private void MoveWall()
        {
            moveTransform.DOLocalMoveX(moveValue, moveDuraction).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
        }

        private void RotateWall()
        {
            rotateTransform.DOLocalRotate(new Vector3(360, 0, 0), rotationDuraction)
            .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
        }
    }
}
