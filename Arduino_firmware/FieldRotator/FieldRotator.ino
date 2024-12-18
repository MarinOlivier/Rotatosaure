#include <Stepper.h>
#include <Preferences.h>

#define RW_MODE false
#define RO_MODE true
#define CAN_REVERSE true

constexpr auto STEPS_PER_REVOLUTION = 2048;
constexpr auto MOTOR_TURNS_PER_CAMERA_TURN = 75;
constexpr auto STEPS_PER_CAMERA_REVOLUTION = STEPS_PER_REVOLUTION * MOTOR_TURNS_PER_CAMERA_TURN;
constexpr auto MOTOR_RPM = 15;

constexpr auto DEVICE_GUID = "7e2006ab-88b5-4b09-b0b3-1ac3ca8da43e";

constexpr auto COMMAND_PING = "COMMAND:PING";
constexpr auto RESULT_PING = "RESULT:PING:OK:";

constexpr auto COMMAND_SETANGLE = "COMMAND:ANGLE:SET";
constexpr auto COMMAND_GETANGLE = "COMMAND:ANGLE:GET";
constexpr auto COMMAND_GOTO = "COMMAND:ANGLE:GOTO";
constexpr auto COMMAND_CANREVERSE = "COMMAND:SETTING:CANREVERSE";
constexpr auto COMMAND_SETREVERSE = "COMMAND:SETTING:SETREVERSE";
constexpr auto COMMAND_GETREVERSE = "COMMAND:SETTING:GETREVERSE";

constexpr auto ERROR_INVALID_COMMAND = "ERROR:INVALID_COMMAND";

// Creates an instance of stepper class
// Pins entered in sequence IN1-IN3-IN2-IN4 for proper step sequence
Stepper myStepper = Stepper(STEPS_PER_REVOLUTION, 5, 3, 4, 2);

// Create instance of Preference class
// Store value in flash memory
Preferences stcPrefs;

// Variables to track the current and target angles
float currentAngle = 90.0; // in degrees
float targetAngle = currentAngle;
int stepsRemaining = 0;
unsigned long lastUpdateTime = 0;
const unsigned long updateInterval = 50; // 50 milliseconds
const int stepsPerUpdate = 2048; // Number of steps to move per update
bool isReverse = false;

// The `setup` function runs once when you press reset or power the board.
void setup() {
    // Initialize serial communication for debugging
    Serial.begin(9600);

    // Set the initial speed of the stepper motor
    myStepper.setSpeed(MOTOR_RPM);

    stcPrefs.begin("STCPrefs", RW_MODE);

    bool tpInit = stcPrefs.isKey("nvsInit");       // Test for the existence
                                                  // of the "already initialized" key.

   if (tpInit == false) {
      // If tpInit is 'false', the key "nvsInit" does not yet exist therefore this
      //  must be our first-time run. We need to set up our Preferences namespace keys. So...

      // The .begin() method created the "STCPrefs" namespace and since this is our
      //  first-time run we will create
      //  our keys and store the initial "factory default" values.
      stcPrefs.putLong("curAngle", currentAngle);
      stcPrefs.putBool("isReverse", isReverse);

      stcPrefs.putBool("nvsInit", true);          // Create the "already initialized"
                                                  //  key and store a value.
   }

   // Retrieve the operational parameters from the namespace
   //  and save them into their run-time variables.
   currentAngle = stcPrefs.getLong("curAngle");
   isReverse = stcPrefs.getBool("isReverse");
}

// The `loop` function runs over and over again until power down or reset.
void loop() {
    if (Serial.available() > 0) {
        String command = Serial.readStringUntil('\n');

        if (command.startsWith(COMMAND_PING)) {
            handlePing();
        }
        else if (command.startsWith(COMMAND_SETANGLE)) { // SETANGLE
            String angleStr = command.substring(command.lastIndexOf(':') + 1);
            float angle = angleStr.toFloat();
            setCurrentAngle(angle);
        }
        else if (command.startsWith(COMMAND_GOTO)) { // GOTO
            String angleStr = command.substring(command.lastIndexOf(':') + 1);
            float angle = angleStr.toFloat();
            goToAngle(angle);
        }
        else if (command.startsWith(COMMAND_GETANGLE)) { // GET POSITION
            Serial.println(currentAngle);
        }
        else if (command.startsWith(COMMAND_CANREVERSE)) { // GET CAN REVERSE
            Serial.println(CAN_REVERSE);
        }
        else if (command.startsWith(COMMAND_SETREVERSE)) { // SET REVERSE
            String isReverseStr = command.substring(command.lastIndexOf(':') + 1);
            isReverse = isReverseStr.equalsIgnoreCase("true");
            stcPrefs.putBool("isReverse", isReverse);
        }
        else if (command.startsWith(COMMAND_GETREVERSE)) { // GET REVERSE
            Serial.println(stcPrefs.getBool("isReverse"));
        }
        else {
            handleInvalidCommand();
        }
    }

    // Update the motor position in the background
    updateMotorPosition();
    stcPrefs.putLong("curAngle", currentAngle);
}

//-- COMMAND HANDLING ------------------------------------------------------

void handlePing() {
    Serial.print(RESULT_PING);
    Serial.println(DEVICE_GUID);
}

void setCurrentAngle(float angle) {
    currentAngle = fmod(angle, 360.0);
    if (currentAngle < 0) {
        currentAngle += 360.0;
    }
    Serial.print("Current Angle Set to: ");
    Serial.println(currentAngle);
}

void goToAngle(float newTargetAngle) {
    targetAngle = fmod(newTargetAngle, 360.0);
    if (targetAngle < 0) {
        targetAngle += 360.0;
    }

    // Calculate the shortest rotation
    float delta = targetAngle - currentAngle;
    delta = fmod(delta + 360.0, 360.0);
    if (delta > 180.0) {
        delta -= 360.0;
    }

    // Calculate the number of steps to move
    stepsRemaining = delta * (STEPS_PER_CAMERA_REVOLUTION / 360.0);

    // Reverse the direction if isReverse is true
    if (isReverse) {
        stepsRemaining = -stepsRemaining;
    }

    lastUpdateTime = millis();
}

void updateMotorPosition() {
    if (stepsRemaining != 0 && millis() - lastUpdateTime >= updateInterval) {
        int stepsToMove = stepsRemaining > 0 ? stepsPerUpdate : -stepsPerUpdate;
        if (abs(stepsRemaining) < stepsPerUpdate) {
            stepsToMove = stepsRemaining;
        }

        myStepper.step(stepsToMove);
        stepsRemaining -= stepsToMove;

        // Update currentAngle based on the actual movement direction
        currentAngle += stepsToMove * (360.0 / STEPS_PER_CAMERA_REVOLUTION);
        currentAngle = fmod(currentAngle, 360.0);
        if (currentAngle < 0) {
            currentAngle += 360.0;
        }

        lastUpdateTime = millis();
    }
}



void handleInvalidCommand() {
    Serial.println(ERROR_INVALID_COMMAND);
}
