using DG.Tweening;
using UnityEngine;

namespace Hyper_Casuel_Obstacle
{
    public class CameraMovement : MonoBehaviour
    {
        [SerializeField] float finishPos;
        [SerializeField] float duraction;

        private void Start()
        {
            gameObject.transform.DOLocalMoveZ(finishPos, duraction).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.Linear).SetRelative(true);
        }
    }
}
