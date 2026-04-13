using UnityEngine;
using DG.Tweening;


namespace Hyper_Casuel_Obstacle
{
    public class GuillotineAnimation : MonoBehaviour
    {
        [SerializeField] private Transform guillotine;

        [SerializeField] private float downDuraction;
        [SerializeField] private float upDuraction;
        [SerializeField] private float downTransform;
        [SerializeField] private float upTransform;

        private void Start()
        {
            StartAnimate();
        }
        private void StartAnimate()
        {
            Tween goDownTween = guillotine.DOLocalMoveY(downTransform, downDuraction).SetEase(Ease.OutBounce);
            Tween goUpTween = guillotine.DOLocalMoveY(upTransform, upDuraction).SetEase(Ease.OutQuint);

            Sequence movementSequence = DOTween.Sequence();

            movementSequence.Append(goDownTween).AppendInterval(0.25f)
            .Append(goUpTween).AppendInterval(0.75f)
            .SetLoops(-1, LoopType.Restart);
        }
    }
}