# Android toolchain (JDK 17 + SDK in the home directory, no sudo). Source after dev-env.sh.
export JAVA_HOME="$HOME/jdk/jdk-17.0.21.0.2+1"
export ANDROID_HOME="$HOME/android-sdk"
export ANDROID_SDK_ROOT="$ANDROID_HOME"
export PATH="$JAVA_HOME/bin:$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$PATH"
