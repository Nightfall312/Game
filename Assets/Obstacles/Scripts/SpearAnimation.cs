using DG.Tweening;
using UnityEngine;

namespace Hyper_Casuel_Obstacle
{
    public class SpearAnimation : MonoBehaviour
    {
        [SerializeField] private Transform spear;
        [SerializeField] private float moveUpValue;
        [SerializeField] private float moveDownValue;
        [SerializeField] private float spearGoUpDuraction;
        [SerializeField] private float spearGoDownDuraction;
        [SerializeField] private float upWaitTime;
        [SerializeField] private float DownWaitTime;


        private void Start()
        {
            StartAnimation();
        }

        private void StartAnimation()
        {
            Tween spearUp = spear.DOLocalMoveY(moveUpValue, spearGoUpDuraction).SetEase(Ease.Linear).SetRelative(true);
            Tween spearDown = spear.DOLocalMoveY(moveDownValue, spearGoDownDuraction).SetEase(Ease.InExpo);

            Sequence spearSequence = DOTween.Sequence();

            spearSequence.Append(spearUp).AppendInterval(upWaitTime).Append(spearDown).AppendInterval(DownWaitTime).SetLoops(-1, LoopType.Restart);
        }

    }
}