using UnityEngine;
using DG.Tweening;

namespace Hyper_Casuel_Obstacle
{
    public class WallAnimation01 : MonoBehaviour
    {
        [SerializeField] private Transform wall;

        [SerializeField] private float moveValue;
        [SerializeField] private float moveDuraction;

        private void Start()
        {
            StartAnimation();
        }
        private void StartAnimation()
        {
            Tween launchWall = wall.DOLocalMoveZ(moveValue, moveDuraction).SetEase(Ease.OutElastic);
            Tween goFrame = wall.DOLocalMoveZ(0.3f, moveDuraction).SetEase(Ease.InOutQuint);

            Sequence wallSequence = DOTween.Sequence();

            wallSequence.Append(launchWall).AppendInterval(0.5f).Append(goFrame).AppendInterval(1f)
            .SetLoops(-1, LoopType.Restart);


        }

    }
}
