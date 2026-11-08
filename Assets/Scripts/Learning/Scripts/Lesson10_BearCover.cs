using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson10_BearCover : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        string[] names = { "Pine", "Bush", "Rock", "Field", "Log" };
        float[] distances = { 35f, 18f, 42f, 25f, 22f };
        float[] cover = { 0.9f, 0.5f, 1.0f, 0f, 0.7f };
        float minCover = 0.95f;
        float maxDistance = 40f;
        int suitableCount = 0;
        float bestDistance = 9999f;
        int bestIndex = -1;

        for (int i = 0; i < names.Length; i++)
        {
            if (cover[i] >= minCover && distances[i] <= maxDistance)
            {
                suitableCount++;
                if (distances[i] < bestDistance)
                {
                    bestDistance = distances[i];
                    bestIndex = i;
                }
            }
            
        }
        Debug.Log("Suitable: " + suitableCount);   // лічильник придатних точок
        if (bestIndex == -1)                       // жодна точка не підійшла
        {
            Debug.Log("No hiding place");
        }
        else
        {
            Debug.Log("Best: " + names[bestIndex] + " (" + bestDistance + " m)");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
