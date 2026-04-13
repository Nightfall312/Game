using UnityEngine;
using DG.Tweening;
using UnityEngine.Serialization;

namespace Hyper_Casuel_Obstacle
{
    public class HorizontalStick02 : MonoBehaviour
    {
        [SerializeField] Transform stickArm;
        [SerializeField] Transform rollingStick;
        [SerializeField] float yValue;
        [SerializeField] float moveDuraction;
        [SerializeField] float rollDuraction;
        [SerializeField] bool moveReverse;
        [SerializeField] bool rollReverse;
        private void Start()
        {
            StartMoving();
            StartRolling();
        }

        private void StartMoving()
        {
            if (moveReverse)
            {
                stickArm.transform.DOLocalMoveY(-yValue, moveDuraction).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetRelative(true);
            }
            else
            {
                stickArm.transform.DOLocalMoveY(yValue, moveDuraction).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetRelative(true);
            }
        }

        private void StartRolling()
        {
            if (rollReverse)
            {
                var rotation = new Vector3(-360, 0, 0);
                rollingStick.DOLocalRotate(rotation, rollDuraction, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
            }
            else
            {
                var rotation = new Vector3(360, 0, 0);
                rollingStick.DOLocalRotate(rotation, rollDuraction, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
            }
        }
    }
}