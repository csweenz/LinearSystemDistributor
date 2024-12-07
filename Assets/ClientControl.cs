using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using System.Collections.Generic;

public class ClientControl : NetworkBehaviour
{    
//UI and Game Flow
    public GameObject sliderPrefab;
    public GameObject sliderLabelPrefab;
    public GameObject inputFieldPrefab;
    public Transform sliderDisplay;
    public Transform sliderLabelDisplay;
    public Transform inputFieldDisplay;
    public Button confirmButton;
    public Button autoGuessButton;
    public Toggle autoGuessToggle;
    public Text promptText;
    private GameObject[] sliderCells;
    private GameObject[] sliderLabels;
    private GameObject[] inputFields;
    private float[] guessVector;
    public MatrixVisualizer matrixVisualizer;


//Algorithm variables:  x_i(t+1) = x_i(t) - (P_i/(m_i(t)))(m_i(t)*x_i(t) - Ex_j(t))
    private bool[] isSolved;
    private List<ulong> neighbors = new List<ulong>(); //m_i(t)
    private Dictionary<ulong, float[]> neighborGuesses = new Dictionary<ulong, float[]>(); //used to calculate Ex_j(t)
    public ulong ClientId;
    private float[] lastAverageGuess; //Ex_j(t)
    private bool autoGuessEveryTurn = false;
    public float currentGuess; // Represents x_i(t)
    public float projectionParameter = 0.01f; // Represents P_i
    private int iterationStep = 0;


//Client Logic
    public override void OnNetworkSpawn()
    {
        Debug.Log("Client spawned...");
        if (IsClient && !IsServer)
        {
            confirmButton.onClick.AddListener(OnConfirmButtonClicked);
            autoGuessButton.onClick.AddListener(OnAutoGuessButtonClicked);
            autoGuessToggle.onValueChanged.AddListener(OnAutoGuessToggleChanged);
            matrixVisualizer.currentState = MatrixVisualizer.GameState.Connecting;

            UpdatePrompt("Connecting to server. Waiting for the server to set up the matrix...");
        }
    }

  public void OnMatrixSetupComplete()
    {
        if (!IsClient || IsServer)
        {
        Debug.Log("OnMatrixSetupComplete() should only be executed by clients, not the server.");
        return;
        }
        // Called when RPCs notify the client that the matrix is ready
        matrixVisualizer.currentState = MatrixVisualizer.GameState.ViewingMatrix;
    if (matrixVisualizer == null)
    {
        Debug.LogError("matrixVisualizer is null!");
    }
    if (matrixVisualizer.matrixDisplay == null)
    {
        Debug.LogError("matrixDisplay is null!");
    }

    if (matrixVisualizer.matrixCellPrefab == null)
    {
        Debug.LogError("matrixCellPrefab is null!");
    }
        //convert network list from MatrixVisualizer to float array
    float[] resultArray = new float[matrixVisualizer.coefficientList.Count];
        for (int i = 0; i < matrixVisualizer.coefficientList.Count; i++)
        {
            resultArray[i] = matrixVisualizer.coefficientList[i];
        }
    matrixVisualizer.DisplayClientView();

    matrixVisualizer.GenerateMatrix(matrixVisualizer.totalRows.Value, matrixVisualizer.totalColumns.Value);
    matrixVisualizer.SetupGridLayout(matrixVisualizer.totalRows.Value, matrixVisualizer.totalColumns.Value);
        //update client cells
            for (int i = 0; i < matrixVisualizer.totalRows.Value; i++)
            {
               for (int j = 0; j < matrixVisualizer.totalColumns.Value; j++)
                {
                int index = i * matrixVisualizer.totalColumns.Value + j;
                matrixVisualizer.UpdateCellValue(i, j, resultArray[index]);
                }
            }
    matrixVisualizer.UpdateAugmentedMatrix(matrixVisualizer.augmentedVectorB);
        Debug.Log($"Generated matrix on client: {matrixVisualizer.totalRows.Value} rows x {matrixVisualizer.totalColumns.Value} columns");

        // Transition to AdjustingSliders state and AutoGuess if toggle is checked
    ActivateSliders();
    matrixVisualizer. currentState = MatrixVisualizer.GameState.AdjustingSliders;
        Debug.Log("Adjust the sliders and press confirm to submit your guess.");

    if (autoGuessEveryTurn && matrixVisualizer.currentState == MatrixVisualizer.GameState.AdjustingSliders)
        {
            SubmitGuessServerRpc(GetGuessVector());
        }
    }

  private void OnConfirmButtonClicked()
    {
        switch (matrixVisualizer.currentState)
        {
            case MatrixVisualizer.GameState.ViewingMatrix:
                 matrixVisualizer. currentState = MatrixVisualizer.GameState.AdjustingSliders;
                break;

            case MatrixVisualizer.GameState.AdjustingSliders:
                // Gather guess from sliders and send to the server
                guessVector = GetGuessVector();
                SubmitGuessServerRpc(guessVector);
                confirmButton.interactable = false;
                Debug.Log("Guess submitted. Waiting for the server's response...");
                break;

        }
    }

  private void OnAutoGuessButtonClicked()
    {
        ApplyAverageGuess();
        Debug.Log("Auto-guess applied using the average values.");
    }

  private void OnAutoGuessToggleChanged(bool isOn)
    {
    autoGuessEveryTurn = isOn;

    // Disable manual buttons when auto-guess is enabled
    autoGuessButton.interactable = !isOn;
    confirmButton.interactable = !isOn;

    Debug.Log("Auto-guess every turn: " + autoGuessEveryTurn);

    // If auto-guess is enabled mid-game, automatically start the guessing process
    if (autoGuessEveryTurn && matrixVisualizer.currentState == MatrixVisualizer.GameState.AdjustingSliders)
    {
        SubmitGuessServerRpc(GetGuessVector());
    }
}

  private void ApplyAverageGuess() 
    {
        if (matrixVisualizer.currentState == MatrixVisualizer.GameState.AdjustingSliders && lastAverageGuess != null)
        {
            for (int i = 0; i < sliderCells.Length; i++)
            {
                Slider slider = sliderCells[i].GetComponent<Slider>();
                slider.value = lastAverageGuess[i];
            }
        }
    }

public void SetNeighbors(ulong[] neighborIds)
{
     if (!IsClient || IsServer)
        {
        Debug.Log("SetNeighbors should only be executed by clients, not the server.");
        return;
        }
    neighbors.Clear();
    neighbors.AddRange(neighborIds);
    Debug.Log($"Neighbors assigned: {string.Join(",", neighbors)}");
}

public void UpdateNeighborGuesses(Dictionary<ulong, float[]> guesses)
{
     if (!IsClient || IsServer)
        {
        Debug.Log("UpdateNeighborGuesses should only be executed by clients, not the server.");
        return;
        }
        Debug.Log("Guesses:  "+ guesses.Values.Count);
    neighborGuesses = guesses;
    Debug.Log($"Updated neighbor guesses for client {ClientId}");
}

private float GetNeighborValue(ulong neighborId, int index)
{
    if (neighborGuesses.ContainsKey(neighborId))
    {
        float[] guess = neighborGuesses[neighborId];
        if (index >= 0 && index < guess.Length)
        {
            return guess[index];
        }
    }
    Debug.LogError($"Neighbor {neighborId} or index {index} not found.");
    return 1f; // Default value if the neighbor or index is not found
}

public void ApplyAutoGuess(float[] averageGuess, float[] errorVector)
{
    if (matrixVisualizer.currentState == MatrixVisualizer.GameState.AdjustingSliders && lastAverageGuess != null)
    {
        // Retrieve the current values of the sliders (the current guess vector)
        float[] currentValues = GetGuessVector();
        int dimension = currentValues.Length;

        iterationStep++;  //Track algorithm progression

        float initialLearningRate = 0.1f; // Controls how quickly the guesses converge
        float fixedStepSize = 1f; // Forces incremental change to convergence

        // Calculate the difference vector between the current values and the average guess
        float[] differenceVector = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            differenceVector[i] = averageGuess[i] - currentValues[i]; // should divide by neighbors.Count
        }

        // Calculate the dot product between the current guess vector and the difference vector
        float dotProduct = 0f;
        for (int i = 0; i < dimension; i++)
        {
            dotProduct += currentValues[i] * differenceVector[i];
        }

        // Use neighbor count and the dot product to influence the projection scaling
        float projectionScalingFactor = projectionParameter * dotProduct; // Should divide by neighbors.Count

        // Update each value in the guess vector based on the projection scaling factor, difference, and fixed step size
        for (int i = 0; i < dimension; i++)
        {
            // Skip any sliders that have already been solved
            if (isSolved[i])
            {
                continue;
            }

            // Ensure error remains positive and doesn't approach zero (avoiding division by zero)
            float error = Mathf.Abs(errorVector[i]);
            error = Mathf.Max(error, 0.001f); // Set a minimum value for error to prevent instability

            // Calculate the adaptive scaling factor based on the current error
            float adaptiveScalingFactor = initialLearningRate / (1 + error); // As error decreases, step size becomes smaller

            // Adjust the current value to move towards the average guess using adaptive scaling
            currentValues[i] += adaptiveScalingFactor * differenceVector[i];

            // Apply the projection scaling factor to the current value
            currentValues[i] += projectionScalingFactor * differenceVector[i];

            // Apply fixed step size incrementally to further correct the error
            if (errorVector[i] > 0)
            {
                // Decrease value if error is positive
                currentValues[i] = Mathf.Clamp(currentValues[i] - fixedStepSize, sliderCells[i].GetComponent<Slider>().minValue, sliderCells[i].GetComponent<Slider>().maxValue);
            }
            else if (errorVector[i] < 0)
            {
                // Increase value if error is negative
                currentValues[i] = Mathf.Clamp(currentValues[i] + fixedStepSize, sliderCells[i].GetComponent<Slider>().minValue, sliderCells[i].GetComponent<Slider>().maxValue);
            }

            // Clamp the value again to ensure no boundary violations occur
            currentValues[i] = Mathf.Clamp(currentValues[i], sliderCells[i].GetComponent<Slider>().minValue, sliderCells[i].GetComponent<Slider>().maxValue);
        }

        // Apply updated values to sliders
        UpdateSliderWithGuess(currentValues);
    }
}


    public void SetAutoGuessToggle(bool autoGuessEnabled)
{
    if (!IsClient || IsServer)
        {
        Debug.Log("SetAutoGuessToggle should only be executed by clients, not the server.");
        return;
        }
    autoGuessToggle.isOn = autoGuessEnabled;
    autoGuessEveryTurn = autoGuessEnabled; 
}

private void UpdateSliderWithGuess(float[] newGuess)
{
    if (sliderCells == null || sliderCells.Length == 0)
    {
        Debug.LogError("No sliders available to update.");
        return;
    }

    for (int i = 0; i < sliderCells.Length; i++)
    {
        if (sliderCells[i] == null)
        {
            Debug.LogError($"Slider cell at index {i} is null.");
            continue;
        }

        Slider slider = sliderCells[i].GetComponent<Slider>();
        if (slider == null)
        {
            Debug.LogError($"No Slider component found on slider cell at index {i}.");
            continue;
        }

        slider.value = (int)newGuess[i]; // Set the slider to the new auto-guess value
        //slider.onValueChanged.Invoke(newGuess[i]); // Force UI update, if needed
    }
}


    private void ActivateSliders()
{
    if (!IsClient || IsServer)
    {
        Debug.Log("ActivateSliders() should only be executed by clients, not the server.");
        return;
    }
    int numSliders = matrixVisualizer.totalColumns.Value;
    sliderCells = new GameObject[numSliders];
    sliderLabels = new GameObject[numSliders];
    inputFields = new GameObject[numSliders];
    isSolved = new bool[numSliders];

    System.Random random = new System.Random(); // For generating random initial values

    for (int i = 0; i < numSliders; i++)
    {
        // Instantiate slider
        GameObject sliderObj = Instantiate(sliderPrefab, sliderDisplay);
        Slider slider = sliderObj.GetComponent<Slider>();
        slider.minValue = -100;
        slider.maxValue = 100;

        // Set initial value: either 0 or random value if auto-guess is enabled
        if (autoGuessEveryTurn)
        {
            slider.value = random.Next(-100, 101); // Generate random values between -100 and 100
        }
        else
        {
            slider.value = 0;
        }

        slider.wholeNumbers = true; // Allow for floating-point values?  Change listener below
        sliderCells[i] = sliderObj;

        // Listener to enforce steps of 1
        slider.onValueChanged.AddListener(value =>
        {
            float roundedValue = Mathf.Round(value * 1f) / 1f; // Round to the nearest 1 increment
            if (Mathf.Abs(value - roundedValue) > Mathf.Epsilon)
            {
                slider.value = roundedValue; // Set slider rounded value
            }
            Debug.Log($"Slider {i} updated to rounded value: {roundedValue}");
        });

        // Instantiate labels
        GameObject labelObj = Instantiate(sliderLabelPrefab, sliderLabelDisplay);
        Text label = labelObj.GetComponent<Text>();
        label.text = $"X{i + 1}"; // Set label for each slider
        sliderLabels[i] = labelObj;

        // Instantiate input fields
        GameObject inputFieldObj = Instantiate(inputFieldPrefab, inputFieldDisplay);
        InputField inputField = inputFieldObj.GetComponent<InputField>();
        inputField.text = slider.value.ToString(); // Set initial value to match slider
        inputFields[i] = inputFieldObj;

        // Listener to update slider when input field changes
        inputField.onValueChanged.AddListener(value =>
        {
            float newValue;
            if (float.TryParse(value, out newValue))
            {
                newValue = Mathf.Round(newValue * 1f) / 1f; // Ensure input is also rounded
                slider.value = newValue;
            }
        });

        // Listener to update input field when slider changes
        slider.onValueChanged.AddListener(value =>
        {
            inputField.text = value.ToString();
        });
    }

    // If auto-guess is enabled, submit the initial random guess automatically
    if (autoGuessEveryTurn)
    {
        float[] initialGuess = GetGuessVector(); // Get current slider values as the guess vector
        Debug.Log("Auto-guess enabled at game start. Submitting initial random guess.");
        SubmitGuessServerRpc(initialGuess);
    }
}



    private float[] GetGuessVector()
    {
        float[] guessVector = new float[sliderCells.Length];
        for (int i = 0; i < sliderCells.Length; i++)
        {
            Slider slider = sliderCells[i].GetComponent<Slider>();
            guessVector[i] = slider.value;
        }
        return guessVector;
    }

[ServerRpc(RequireOwnership = false)]
    private void SubmitGuessServerRpc(float[] guess, ServerRpcParams rpcParams = default)
    {
        // Call server logic to compare guess
        Debug.Log($"Received guess from client: {rpcParams.Receive.SenderClientId}");
        FindObjectOfType<ServerControl>().CompareGuess(guess, rpcParams.Receive.SenderClientId);
    }

    private void UpdatePrompt(string message)
    {
        promptText.text = message;
    }

    public void NewTurnUI(Color[] colorVector, float[] guess, float[] errorVector, bool[] solvedFlags)
{
    if (!IsClient || IsServer)
    {
        Debug.Log("NewTurnUI should only be executed by clients, not the server.");
        return;
    }

    matrixVisualizer.currentState = MatrixVisualizer.GameState.AdjustingSliders;
    Debug.Log("New Turn started");
    lastAverageGuess = guess;

    // Update UI to reflect how close the guess was and show the average guess
    for (int i = 0; i < sliderCells.Length; i++)
    {
        Slider slider = sliderCells[i].GetComponent<Slider>();
        Image handleImage = slider.handleRect.GetComponent<Image>();
        handleImage.color = colorVector[i]; // Set color based on comparison result

        // Lock slider if it is solved
        if (solvedFlags[i])
        {
            slider.value = guess[i];
            slider.interactable = false;
            Debug.Log($"Slider {i} is solved and locked.");
        }
        else
        {
            slider.interactable = true; // Enable unsolved sliders
        }

        // Update visual of average guess
        GameObject avgIndicator = new GameObject($"AvgIndicator_{i}");
        avgIndicator.transform.SetParent(slider.transform);
        RectTransform avgTransform = avgIndicator.AddComponent<RectTransform>();
        avgTransform.anchorMin = new Vector2(0.5f, 0.5f);
        avgTransform.anchorMax = new Vector2(0.5f, 0.5f);
        avgTransform.pivot = new Vector2(0.5f, 0.5f);
        avgTransform.localScale = Vector3.one;
        avgTransform.sizeDelta = new Vector2(10, 10);

        float sliderRange = slider.maxValue - slider.minValue;
        float normalizedValue = (guess[i] - slider.minValue) / sliderRange;
        float indicatorPosition = normalizedValue * slider.GetComponent<RectTransform>().sizeDelta.x;
        avgTransform.anchoredPosition = new Vector2(indicatorPosition - (slider.GetComponent<RectTransform>().sizeDelta.x / 2), 0);

        Image avgImage = avgIndicator.AddComponent<Image>();
        avgImage.color = Color.blue; // Representing average with a blue color

        // Adjust handle size based on error to visually indicate progress
        float error = errorVector[i];
        if (error != 0)
        {
            handleImage.transform.localScale = Vector3.one * (1 + Mathf.Abs(error) / 100f);
        }
    }

    // Update the solved flags with the latest solved status
    UpdateSolvedFlags(solvedFlags);

    // Automatically apply a guess if auto-guess is enabled
    if (autoGuessEveryTurn)
    {
        ApplyAutoGuess(guess, errorVector);
        SubmitGuessServerRpc(GetGuessVector()); // Automatically submit the guess
    }

    // Allow manual adjustment only if auto-guess is disabled
    UpdatePrompt("Adjust the sliders for the next guess.");
    confirmButton.interactable = !autoGuessEveryTurn;
}


public void UpdateSolvedFlags(bool[] solvedFlags)
{
    if (isSolved == null || isSolved.Length != solvedFlags.Length)
    {
        isSolved = new bool[solvedFlags.Length];
    }

    for (int i = 0; i < solvedFlags.Length; i++)
    {
        isSolved[i] = solvedFlags[i];
        if (isSolved[i])
        {
            Debug.Log($"Slider {i} is now solved and will no longer be adjusted.");
        }
    }
}

public void OnWinnerAnnounced(ulong winnerClientId)
{
    if (!IsClient || IsServer)
        {
        Debug.Log("OnWinnerAnnounced should only be executed by clients, not the server.");
        return;
        }
    string winnerMessage;

    if (NetworkManager.Singleton.LocalClientId == winnerClientId)
    {
        winnerMessage = "Congratulations! You found the correct solution!";
    }
    else
    {
        winnerMessage = $"Client {winnerClientId} has found the correct solution!";
    }

    UpdatePrompt(winnerMessage);
    Debug.Log(winnerMessage);

    // Disable further input since the game is over
    DisableClientInput();
}

private void DisableClientInput()
{
    if (!IsClient || IsServer)
        {
        Debug.Log("DisableClientInput should only be executed by clients, not the server.");
        return;
        }
    foreach (GameObject sliderObj in sliderCells)
    {
        Slider slider = sliderObj.GetComponent<Slider>();
        slider.interactable = false;
    }

    foreach (GameObject inputFieldObj in inputFields)
    {
        InputField inputField = inputFieldObj.GetComponent<InputField>();
        inputField.interactable = false;
    }

    confirmButton.interactable = false;
    autoGuessButton.interactable = false;
    autoGuessToggle.interactable = false;
}

}
