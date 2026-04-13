using UnityEngine;
using DG.Tweening;

namespace Hyper_Casuel_Obstacle
{
    public class HorizontalStick : MonoBehaviour
    {
        [SerializeField] GameObject horizontalStick;
        [SerializeField] float yValue;
        [SerializeField] float duraction;
        [SerializeField] bool reverse;
        private void Start()
        {
            StartMoving(reverse);
        }

        private void StartMoving(bool isReverse)
        {
            if (isReverse)
            {
                horizontalStick.transform.DOLocalMoveY(-yValue, duraction).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetRelative(true);
            }
            else
            {
                horizontalStick.transform.DOLocalMoveY(yValue, duraction).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetRelative(true);
            }
        }
    }
}