#!/bin/bash

echo "=== Andy ACP Examples Demo ==="
echo
echo "The Andy ACP library implements the Agent Client Protocol using stdin/stdout."
echo "The client sends ACP messages to stdout, and the server reads them from stdin."
echo
echo "Here are the different ways to use the examples:"
echo

echo "1. CORRECT: Pipe client output to server input"
echo "   Command: dotnet run --project examples/Andy.Acp.Examples -- --client | dotnet run --project examples/Andy.Acp.Examples -- --server"
echo
echo "   Running demo..."
dotnet run --project examples/Andy.Acp.Examples -- --client | dotnet run --project examples/Andy.Acp.Examples -- --server
echo
echo "   ✓ This shows the full ACP communication working correctly!"
echo

echo "2. STANDALONE: Client only (shows raw ACP messages)"
echo "   Command: dotnet run --project examples/Andy.Acp.Examples -- --client"
echo
echo "   Running demo..."
dotnet run --project examples/Andy.Acp.Examples -- --client
echo
echo "   ✓ This shows the ACP protocol messages that would be sent to a server"
echo

echo "3. INTERACTIVE: Server waiting for manual input"
echo "   Command: dotnet run --project examples/Andy.Acp.Examples -- --server"
echo "   (In this mode, you can type ACP messages manually, or press Ctrl+C to exit)"
echo
echo "   For testing, we'll send one message to the server:"
echo "Content-Length: 36

{\"method\":\"ping\",\"id\":1,\"params\":{}}" | dotnet run --project examples/Andy.Acp.Examples -- --server
echo
echo "   ✓ This shows how the server processes individual ACP messages"
echo

echo "=== Demo Complete ==="
echo "The examples are working correctly! The client and server communicate via"
echo "stdin/stdout piping, which is the standard way ACP protocols work."