#!/bin/bash

echo "Testing interactive Ctrl+C handling..."
echo "This will start the server. Press Ctrl+C to test cancellation."
echo "The server should show 'Shutdown requested... (Ctrl+C pressed)'"
echo ""

# Start server interactively
dotnet run --project examples/Andy.Acp.Examples -- --server