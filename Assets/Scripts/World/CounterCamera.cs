using UnityEngine;

// 계산대 뒤 고정 시점. 마우스 오른쪽 버튼을 누른 채 끌면 고개를 돌린다.
// 왼쪽 클릭은 UI 버튼에 쓰기 때문에 시점 조작은 오른쪽 버튼만 쓴다.
public class CounterCamera : MonoBehaviour
{
    public float sensitivity = 3f;
    public float yawLimit = 70f;
    public float pitchMin = -40f;
    public float pitchMax = 20f;
    public float returnSpeed = 0f; // 0 이면 손을 떼도 시선 유지

    Quaternion baseRot;
    float yaw, pitch;

    void Start() { baseRot = transform.localRotation; }

    void Update()
    {
        if (Input.GetMouseButton(1))
        {
            yaw = Mathf.Clamp(yaw + Input.GetAxis("Mouse X") * sensitivity, -yawLimit, yawLimit);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * sensitivity, pitchMin, pitchMax);
        }
        else if (returnSpeed > 0f)
        {
            yaw = Mathf.MoveTowards(yaw, 0, returnSpeed * Time.deltaTime);
            pitch = Mathf.MoveTowards(pitch, 0, returnSpeed * Time.deltaTime);
        }
        if (Input.GetKeyDown(KeyCode.Space)) yaw = pitch = 0;
        transform.localRotation = Quaternion.Euler(0, yaw, 0) * baseRot * Quaternion.Euler(pitch, 0, 0);
    }
}
