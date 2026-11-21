using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson14_BearTrail : MonoBehaviour
{

    int totalSteps = 0;
    // Start is called before the first frame update
    void Start()
    {
        string[] sounds = { "Step", "Wind", "Branch", "Step", "Branch" };
        float[] distances = { 18f, 5f, 9f, 32f, 26f };
        List<string> toCheck = new List<string>();
        for (int i = 0; i < sounds.Length; i++)
        {
            float score = Score(sounds[i], distances[i]);
            if (score > 0)
            {
                // FIX 2: результат Approach зберігаємо в локальну змінну — так само, як score вище.
                // Було: Approach(distances[i]); — кроки рахувались, але число викидалось,
                // а в Debug.Log ішов totalSteps (сума кроків за ВСІ звуки, а не за цей).
                int steps = Approach(distances[i]);
                toCheck.Add(sounds[i]);
                // FIX 1: назва звуку з масиву sounds[i] замість тексту "Branch"; ", " перед кроками; steps замість totalSteps.
                Debug.Log (sounds[i] + " at " + distances[i] + " m -> " + score + " points, " + steps + " steps");
            }
            else Debug.Log (sounds[i] + " at " + distances[i] + " m -> ignore"); // FIX 1: sounds[i] замість "Wind"; "idnore" -> "ignore"
            // FIX 3: тут стояв Debug.Log("Total steps: ...") — усередині for, тому виводився 5 разів. Перенесено в кінець Start.
        }

        // FIX 4: рядок "To check: N" — кількість елементів списку, без циклу.
        Debug.Log("To check: " + toCheck.Count);

        // FIX 5: прибрано toCheck.Sort(); — сортує список за абеткою (Branch ставав перед Step), в умові цього не було.
        for (int i = 0; i < toCheck.Count; i++)
        {
            Debug.Log("- " + toCheck[i]);
        }

        Debug.Log("Total steps: " + totalSteps); // FIX 3: один раз, після циклу і списку
    }


float Score (string sound, float distance)
    {

        if (distance > 25)
        {
            return 0f;
        }

        if (distance <= 25 && distance >= 10){
        if (sound == "Branch")
        {
            return 3f;
        }
        else if (sound == "Step")
        {
            return 2;
        }
        else
        {
            return 0f;
        }
        }
        if (distance < 10)
        {
            if (sound == "Branch")
        {
            return 3*2f;
        }
        else if (sound == "Step")
        {
            return 2*2f;
        }
        else
        {
            return 0f;
        }
        }
        return 0f;
    }

    int Approach(float distance)
    {
        int steps = 0;
        for (int i = 0; distance > 3; i++)
        {
            distance -=4;
            steps += 1;
            if (steps == 10)
            {
                break;
            }
        }
        totalSteps += steps;

        return steps;
    }
    // Update is called once per frame
    void Update()
    {

    }
}
