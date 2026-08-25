using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 방에 배치되는 CCTV 가상 카메라다.
/// 실제 렌더링은 Main Camera 하나가 담당하고, 이 컴포넌트는 시점 프리셋만 제공한다.
/// </summary>
public class RoomCamera : MonoBehaviour
{
    [SerializeField] private CinemachineCamera virtualCamera;

    public CinemachineCamera VirtualCamera => virtualCamera;
    public bool IsConfigured => virtualCamera != null;

    /// <summary>
    /// CameraDirector가 방 CCTV의 선택 우선순위를 변경할 때 사용하는 단일 진입점이다.
    /// </summary>
    public void SetPriority(int priority)
    {
        if (virtualCamera != null)
        {
            virtualCamera.Priority = priority;
        }
    }

    /// <summary>
    /// 같은 GameObject에 있는 CinemachineCamera를 자동 연결한다.
    /// </summary>
    private void Reset()
    {
        virtualCamera = GetComponent<CinemachineCamera>();
    }
}
