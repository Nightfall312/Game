using UnityEngine;
using DG.Tweening;

namespace Hyper_Casuel_Obstacle
{
    public class HorizontalDoubleGrinder : MonoBehaviour
    {
        [SerializeField] private Transform grinder01;
        [SerializeField] private Transform grinder02;
        [SerializeField] private float duration;

        // Values above 1 slow down the obstacle; below 1 speed it up.
        [SerializeField] private float speedMultiplier = 1.4f;

        Vector3 rotation = new Vector3(360, 0, 0);

        void Start()
        {
            StartAnimation();
        }

        private void StartAnimation()
        {
            float scaledDuration = duration * speedMultiplier;
            grinder01.DOLocalRotate(rotation, scaledDuration, RotateMode.FastBeyond360)
                .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
            grinder02.DOLocalRotate(-rotation, scaledDuration, RotateMode.FastBeyond360)
                .SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
        }
    }
}
