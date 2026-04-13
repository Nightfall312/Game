using DG.Tweening;
using UnityEngine;

namespace Hyper_Casuel_Obstacle
{
    public class PressAnimation : MonoBehaviour
    {
        [SerializeField] Transform press;

        [SerializeField] float collisionValue;
        [SerializeField] float resetValue;

        [SerializeField] float collisionDuraction;
        [SerializeField] float resetDuraction;
        [SerializeField] float collisionWaitDuraction;
        [SerializeField] float resetWaitDuraction;

        private void Start()
        {
            PressAnimationx();
        }

        private void StartAnimation()
        {
            PressAnimationx();
        }

        private void PressAnimationx()
        {
            Tween collisionTween = press.DOLocalMoveZ(collisionValue, collisionDuraction).SetEase(Ease.OutBounce);

            Tween resetTween = press.DOLocalMoveZ(resetValue, resetDuraction).SetEase(Ease.OutBounce);

            Sequence pressSequence = DOTween.Sequence();

            pressSequence.Append(collisionTween).AppendInterval(collisionWaitDuraction).Append(resetTween).AppendInterval(resetWaitDuraction).SetLoops(-1, LoopType.Restart);

        }
    }
}