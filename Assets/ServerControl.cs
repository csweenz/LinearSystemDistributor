using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

public class ServerControl : NetworkBehaviour
{
    // UI Elements
    public InputField inputField;
    public Button confirmButton;
    public Button randomizeCoefficientsButton;
    public Button randomizeSolutionButton;
    public Text promptViewText;
    public Toggle autoGuessAllClientsToggle;
    public MatrixVisualizer matrixVisualizer;

    // Data Structures
    private int totalRows;
    private int totalColumns;
    private int currentRowIndex = 0;
    private int currentColumnIndex = 0;
    private Dictionary<ulong, float[]> clientGuesses = new Dictionary<ulong, float[]>();
    private Dictionary<ulong, bool[]> clientSolvedFlags = new Dictionary<ulong, bool[]>();

    private int guessesReceived = 0;
    private float[] solutionVector;
    private Dictionary<ulong, List<ulong>> neighbors = new Dictionary<ulong, List<ulong>>();

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            autoGuessAllClientsToggle.onValueChanged.AddListener(OnAutoGuessAllClientsToggleChanged);
            Debug.Log("ServerControl OnNetworkSpawn called, setting up button listener...");
            confirmButton.onClick.AddListener(OnConfirmInput);
            randomizeCoefficientsButton.onClick.AddListener(OnRandomizeCoefficientsButtonClicked);
            randomizeSolutionButton.onClick.AddListener(OnRandomizeSolutionButtonClicked);
            StartGameSetupFlow();
        }
    }

    private void StartGameSetupFlow()
    {
        matrixVisualizer.currentState = MatrixVisualizer.GameState.SetRows;
        Debug.Log("Game setup flow started. Prompting user to enter number of rows...");
        UpdatePrompt("Enter the number of rows:");
    }

    private void OnAutoGuessAllClientsToggleChanged(bool isOn)
    {
        if (IsServer)
        {
            NotifyClientsAutoGuessChangedClientRpc(isOn);
        }
    }

    [ClientRpc]
    private void NotifyClientsAutoGuessChangedClientRpc(bool autoGuessEnabled)
    {
        // This method called on every client
        ClientControl clientControl = FindObjectOfType<ClientControl>();
        if (clientControl != null)
        {
            clientControl.SetAutoGuessToggle(autoGuessEnabled);
        }
    }

    private void OnConfirmInput()
    {
        Debug.Log("Confirmed...");
        switch (matrixVisualizer.currentState)
        {
            case MatrixVisualizer.GameState.SetRows:
                if (int.TryParse(inputField.text, out totalRows))
                {
                    Debug.Log($"Number of rows set to: {totalRows}");
                    matrixVisualizer.SetTotalRows(totalRows);
                    matrixVisualizer.currentState = MatrixVisualizer.GameState.SetColumns;
                    UpdatePrompt("Enter the number of columns:");
                }
                else
                {
                    UpdatePrompt("Invalid input. Please enter a valid number of rows:");
                }
                break;

            case MatrixVisualizer.GameState.SetColumns:
                if (int.TryParse(inputField.text, out totalColumns))
                {
                    matrixVisualizer.SetTotalColumns(totalColumns);
                    matrixVisualizer.currentState = MatrixVisualizer.GameState.SetCoefficients;
                    currentRowIndex = 0;
                    currentColumnIndex = 0;
                    matrixVisualizer.SetupGridLayout(totalRows, totalColumns);
                    matrixVisualizer.GenerateMatrix(totalRows, totalColumns);
                    UpdatePrompt($"Enter coefficient A(1,1):");
                }
                else
                {
                    UpdatePrompt("Invalid input. Please enter a valid number of columns:");
                }
                break;

            case MatrixVisualizer.GameState.SetCoefficients:
                if (float.TryParse(inputField.text, out float coefficientValue))
                {
                    // Update matrix with manually entered coefficient
                    UpdateMatrixWithManualInput(coefficientValue);
                }
                else
                {
                    UpdatePrompt("Invalid input. Please enter a valid coefficient value:");
                }
                break;

            case MatrixVisualizer.GameState.SetSolution:
                if (float.TryParse(inputField.text, out float solutionValue))
                {
                    if (currentColumnIndex < totalColumns)
                    {
                        solutionVector[currentColumnIndex] = solutionValue;
                        currentColumnIndex++;
                        UpdatePrompt($"Enter solution value x{currentColumnIndex + 1}:");
                    }
                    else
                    {
                        CalculateAugmentedVectorB();
                        matrixVisualizer.SetSolutionVector(solutionVector);
                        matrixVisualizer.currentState = MatrixVisualizer.GameState.ConfirmSetup;
                        UpdatePrompt("Game board setup complete. Press confirm to continue.");
                    }
                }
                else
                {
                    UpdatePrompt("Invalid input. Please enter a valid solution value:");
                }
                break;

            case MatrixVisualizer.GameState.ConfirmSetup:
                matrixVisualizer.currentState = MatrixVisualizer.GameState.StartGame;
                UpdatePrompt("Press confirm to start the game.");
                break;

            case MatrixVisualizer.GameState.StartGame:
                StartGame();
                break;

             case MatrixVisualizer.GameState.ViewingMatrix:
                // Transition to AdjustingSliders state
                matrixVisualizer. currentState = MatrixVisualizer.GameState.AdjustingSliders;
                break;
        }
    }

    private void OnRandomizeCoefficientsButtonClicked()
    {
        Debug.Log("Randomizing all coefficients...");
        for (int row = 0; row < totalRows; row++)
        {
            for (int col = 0; col < totalColumns; col++)
            {
                float randomValue = Random.Range(-100.0f, 100.0f);
                matrixVisualizer.UpdateMatrixData(row, col, randomValue);
            }
        }

        // Update the current input prompt to reflect the randomized state
        UpdatePrompt("All coefficients have been randomized. Press confirm to continue or enter manual adjustments.");
        currentRowIndex = totalRows;
        currentColumnIndex = totalColumns -1;
    }

    private void OnRandomizeSolutionButtonClicked()
    {
        Debug.Log("Randomizing all solution terms...");
        solutionVector = new float[totalColumns]; // Ensure solution vector has the correct length
        for (int i = 0; i < totalColumns; i++)
        {
            solutionVector[i] = Random.Range(-100.0f, 100.0f);
        }

        // Update the solution vector in the MatrixVisualizer
        matrixVisualizer.SetSolutionVector(solutionVector);

        // Update the prompt to reflect the new randomized solution state
        UpdatePrompt("Solution terms have been randomized. Press confirm to continue or enter manual adjustments.");
        currentColumnIndex = totalColumns;
    }


    private void UpdateMatrixWithManualInput(float coefficientValue)
    {
        matrixVisualizer.UpdateMatrixData(currentRowIndex, currentColumnIndex, coefficientValue);
        currentColumnIndex++;

        if (currentColumnIndex >= totalColumns)
        {
            currentRowIndex++;
            currentColumnIndex = 0;
        }

        if (currentRowIndex < totalRows)
        {
            UpdatePrompt($"Enter coefficient A({currentRowIndex + 1},{currentColumnIndex + 1}):");
        }
        else
        {
            matrixVisualizer.currentState = MatrixVisualizer.GameState.SetSolution;
            solutionVector = new float[totalColumns];
            currentColumnIndex = 0;
            UpdatePrompt("Enter solution value x1:");
        }
    }



    private void StartGame()
    {
        Debug.Log("Game started!");
        //AssignNeighbors();
        NotifyClientsMatrixSetupCompleteClientRpc();
    }

    [ClientRpc]
    private void NotifyClientsMatrixSetupCompleteClientRpc()
    {
        Debug.Log("Notifying clients that the matrix setup is complete...");
        FindObjectOfType<ClientControl>()?.OnMatrixSetupComplete();
    }

    private void CalculateAugmentedVectorB()
    {
        float[] augmentedVectorB = new float[totalRows];
        for (int i = 0; i < totalRows; i++)
        {
            augmentedVectorB[i] = 0;
            for (int j = 0; j < totalColumns; j++)
            {
                augmentedVectorB[i] += matrixVisualizer.GetCoefficient(i, j) * solutionVector[j];
            }
        }
        matrixVisualizer.SetAugmentedVectorB(augmentedVectorB);
    }

    private void UpdatePrompt(string message)
    {
        promptViewText.text = message;
    }

    public void CompareGuess(float[] guess, ulong clientId)
{
    bool isExactMatch = true;

    // Initialize solved flags for the client if not already done
    if (!clientSolvedFlags.ContainsKey(clientId))
    {
        clientSolvedFlags[clientId] = new bool[solutionVector.Length];
    }

    bool[] solvedFlags = clientSolvedFlags[clientId];

    // Update solved flags based on this guess
    for (int i = 0; i < solutionVector.Length; i++)
    {
        if (Mathf.Approximately(guess[i], solutionVector[i]))
        {
            solvedFlags[i] = true; // Mark this component as solved
        }
        else
        {
            solvedFlags[i] = false; // Ensure it's marked unsolved if not a match
            isExactMatch = false;   // If any component is not exact, the whole vector isn't an exact match
        }
    }

    // Update the solved flags for this client
    clientSolvedFlags[clientId] = solvedFlags;

    if (isExactMatch)
    {
        Debug.Log($"Client {clientId} found the exact solution!");
        NotifyClientsWinnerClientRpc(clientId);
        return;
    }

    if (clientGuesses.ContainsKey(clientId))
    {
        clientGuesses[clientId] = guess;
    }
    else
    {
        clientGuesses.Add(clientId, guess);
    }

    // Process guesses after all clients have submitted
    if (clientGuesses.Count == NetworkManager.Singleton.ConnectedClients.Count - 1)
    {
        // Iterate over each client and notify them about their updated state
        foreach (var clientEntry in clientGuesses)
        {
            ulong currentClientId = clientEntry.Key;
            float[] currentGuess = clientEntry.Value;
            bool[] currentSolvedFlags = clientSolvedFlags[currentClientId];
            float[] errorVector = new float[solutionVector.Length];
            Color[] colorVector = new Color[solutionVector.Length];

            // Calculate the error for the current guess and determine color feedback
            for (int i = 0; i < solutionVector.Length; i++)
            {
                errorVector[i] = currentGuess[i] - solutionVector[i];

                if (currentSolvedFlags[i])
                {
                    colorVector[i] = Color.yellow; // Exact match
                }
                else if (Mathf.Abs(errorVector[i]) <= 10)
                {
                    colorVector[i] = Color.green; // Close
                }
                else
                {
                    colorVector[i] = Color.red; // Far off
                }
            }

            // Notify each client about updated UI
            
            NotifyClientNewTurnUIClientRpc(currentClientId, colorVector, currentGuess, errorVector, currentSolvedFlags);
        }

        // Update game state and prepare for next round
        matrixVisualizer.currentState = MatrixVisualizer.GameState.ViewingMatrix;
        Debug.Log("Current state is " + matrixVisualizer.currentState.ToString());
        guessesReceived++;
        clientGuesses.Clear();
    }
}

[ClientRpc]
private void NotifyClientNewTurnUIClientRpc(ulong clientId, Color[] colorVector, float[] guess, float[] errorVector, bool[] solvedFlags)
{
    if (NetworkManager.Singleton.LocalClientId == clientId)
    {
        FindObjectOfType<ClientControl>()?.NewTurnUI(colorVector, guess, errorVector, solvedFlags);
    }
}



    [ClientRpc]
    private void NotifyClientsWinnerClientRpc(ulong winnerClientId)
    {
        FindObjectOfType<ClientControl>()?.OnWinnerAnnounced(winnerClientId);
    }

    

public void AssignNeighbors()
{
    var clients = NetworkManager.Singleton.ConnectedClientsList;

    

    for (int i = 0; i < clients.Count; i++)
    {
        ulong clientId = clients[i].ClientId;

        neighbors[clientId] = new List<ulong>();

        

        if (clients.Count > 1)
        {
            for (int j = 0; j < clients.Count; j++)
            {
                if (i != j)
                {
                    neighbors[clientId].Add(clients[j].ClientId);
                }
            }
        }
    }
    
    // Notify each client of their neighbors
    foreach (var neighborEntry in neighbors)
    {
        ulong clientId = neighborEntry.Key;
        ulong[] neighborIds = neighborEntry.Value.ToArray();
        NotifyNeighborsClientRpc(clientId, neighborIds);
    
    }

    Debug.Log("Neighbors assigned.");
}
[ClientRpc]
public void NotifyNeighborsClientRpc(ulong clientId, ulong[] neighbors)
    {
ClientControl clientControl = FindObjectOfType<ClientControl>();
        if (clientControl != null)
        {
            clientControl.SetNeighbors(neighbors);
        }
    }

[ClientRpc]
    private void NotifyClientNeighborGuessesClientRpc(ulong clientId, ulong[] neighborIds, float[][] guesses)
    {
        
        
        if (NetworkManager.Singleton.LocalClientId == clientId)
        {
         if ((neighborIds.Length) != guesses.Length)
         {
            Debug.LogError("Neighbor IDs and guesses array length mismatch.");
            Debug.LogError("Neighbors = " + neighborIds.Length + " and Guesses = " + guesses.Length);
            
         }

        Dictionary<ulong, float[]> neighborGuesses = new Dictionary<ulong, float[]>();
        for (int i = 1; i < neighborIds.Length; i++)
         {
            neighborGuesses.Add(neighborIds[i], guesses[i-1]);
         }

        FindObjectOfType<ClientControl>()?.UpdateNeighborGuesses(neighborGuesses);
        }
    }
}
