using UnityEngine;

public class FreeCamera : MonoBehaviour
{
    [Header("Configuración de Movimiento")]
    public float speed = 10.0f;        // Velocidad de movimiento con las teclas
    public float fastSpeed = 20.0f;    // Velocidad al presionar Shift

    [Header("Configuración del Mouse")]
    public float mouseSensitivity = 2.0f; // Sensibilidad de giro
    
    private float pitch = 0.0f; // Rotación arriba/abajo (Eje X)
    private float yaw = 0.0f;   // Rotación izquierda/derecha (Eje Y)

    void Start()
    {
        // Esto bloquea el cursor del mouse en el centro de la pantalla y lo oculta (opcional pero recomendado)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // --- 1. ROTACIÓN CON EL MOUSE ---
        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        
        // Limitamos la cámara para que no dé vueltas completas hacia arriba o abajo
        pitch = Mathf.Clamp(pitch, -90f, 90f); 

        // Aplicamos la rotación a la cámara
        transform.eulerAngles = new Vector3(pitch, yaw, 0.0f);

        // --- 2. MOVIMIENTO CON EL TECLADO (WASD) ---
        float x = Input.GetAxis("Horizontal"); // Teclas A y D
        float z = Input.GetAxis("Vertical");   // Teclas W y S

        // Calculamos la dirección del movimiento relativa a hacia dónde mira la cámara
        Vector3 moveDirection = new Vector3(x, 0, z);

        // Si presionamos Shift, vamos más rápido
        float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? fastSpeed : speed;

        // Aplicamos el movimiento
        transform.Translate(moveDirection * currentSpeed * Time.deltaTime);
    }
}