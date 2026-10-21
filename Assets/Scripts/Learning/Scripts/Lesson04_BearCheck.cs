using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson04_BearCheck : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        float distance = 15f;
        int stamina = 40;
        int lives = 1;

        if (distance <= 2.5f)
        {
            Debug.Log("Bear is right next to you!");
        }
        else if (distance <= 10f)
        {
            Debug.Log("Bear hears your steps");
        }
        else
        {
            Debug.Log("Bear is far away");
        }

        if (stamina == 0)
        {
            Debug.Log("No stamina");
        }
        else if (stamina < 50)
        {
            Debug.Log("Too tired to run");
        }
        else
        {
            Debug.Log("You can run");
        }

        if (lives != 0)
        {
            Debug.Log("Game continues");
        }
        else 
        {
            Debug.Log("Game Over");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
