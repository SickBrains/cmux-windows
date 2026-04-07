#!/bin/bash
# Send a command to the sandbox relay and read the result
# Usage: ./tools/sandbox-send.sh "cmux status"
#        ./tools/sandbox-send.sh "screenshot"

CMD_FILE="tools/sandbox-cmd.txt"
RESULT_FILE="tools/sandbox-result.txt"

# Clear old result
rm -f "$RESULT_FILE"

# Write command
echo "$1" > "$CMD_FILE"

# Wait for result (up to 60 seconds)
for i in $(seq 1 120); do
    sleep 0.5
    if [ -f "$RESULT_FILE" ]; then
        cat "$RESULT_FILE"
        exit 0
    fi
done

echo "TIMEOUT: no response from sandbox relay"
exit 1
