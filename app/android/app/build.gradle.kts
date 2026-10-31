import java.util.Base64
import java.util.Properties

plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

// Release signing key: android/key.properties (git-ignored) with storeFile,
// storePassword, keyAlias, keyPassword. See docs/deploy.md "Android release
// key". Without it a release build fails (velaReleaseProblems below).
val keystorePropertiesFile = rootProject.file("key.properties")
val keystoreProperties = Properties().apply {
    if (keystorePropertiesFile.exists()) keystorePropertiesFile.inputStream().use { load(it) }
}
val hasReleaseKey = keystorePropertiesFile.exists()

/// The Branding placeholder domain; a release must not ship with it.
val placeholderDomain = "vela.example"
val linksDomainProperty = project.findProperty("linksDomain") as String?

android {
    namespace = "dev.vela.vela_app"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "dev.vela.app"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        // sentry_flutter 10 requires API 26 (Android 8.0); Flutter's default is 24.
        minSdk = 26
        targetSdk = flutter.targetSdkVersion
        // Uses the version code from pubspec.yaml. When using split APKs, 1000 * ABI_VERSION
        // is added automatically by Flutter. (https://developer.android.com/studio/build/configure-apk-splits#configure-APK-versions)
        // You can force using the value of versionCode by specifying the `-P force-version-code-ignoring-abi=true`
        // flag during build.
        versionCode = flutter.versionCode
        versionName = flutter.versionName
        // Host of the app links (https://<linksDomain>/s/<workId>). OWNER TODO:
        // pass -PlinksDomain=<real domain> (or set it in ~/.gradle/gradle.properties)
        // for release builds; keep it equal to --dart-define=LINKS_DOMAIN. The
        // default is the Branding placeholder and will not verify.
        manifestPlaceholders["linksDomain"] = linksDomainProperty ?: placeholderDomain
    }

    signingConfigs {
        create("release") {
            if (hasReleaseKey) {
                keyAlias = keystoreProperties.getProperty("keyAlias")
                keyPassword = keystoreProperties.getProperty("keyPassword")
                storeFile = keystoreProperties.getProperty("storeFile")?.let { file(it) }
                storePassword = keystoreProperties.getProperty("storePassword")
            }
        }
    }

    buildTypes {
        release {
            // The debug key only when explicitly allowed for a local check
            // (velaLocalRelease=true); the release guard below fails otherwise.
            signingConfig = signingConfigs.getByName(if (hasReleaseKey) "release" else "debug")
        }
    }
}

/// Values passed with --dart-define (Flutter hands them to Gradle as
/// base64 KEY=VALUE entries in the dart-defines property).
fun dartDefines(): Map<String, String> {
    val raw = project.findProperty("dart-defines") as String? ?: return emptyMap()
    return raw.split(",").filter { it.isNotBlank() }.mapNotNull {
        val entry = try {
            String(Base64.getDecoder().decode(it))
        } catch (e: IllegalArgumentException) {
            return@mapNotNull null
        }
        val i = entry.indexOf('=')
        if (i <= 0) null else entry.substring(0, i) to entry.substring(i + 1)
    }.toMap()
}

/// Final review I-5: what makes this release unfit to ship. Empty = fine.
fun velaReleaseProblems(): List<String> {
    val problems = mutableListOf<String>()
    val defines = dartDefines()
    val localRelease = (project.findProperty("velaLocalRelease") as String?) == "true"
    if (!hasReleaseKey && !localRelease) {
        problems += "no android/key.properties: the release would be signed with the debug key " +
            "(see docs/deploy.md 'Android release key'; -PvelaLocalRelease=true only for a local check)"
    }
    val links = linksDomainProperty
    if (links.isNullOrBlank() || links.contains(placeholderDomain)) {
        problems += "Gradle property linksDomain is unset or the placeholder $placeholderDomain (-PlinksDomain=<links domain>)"
    }
    val definedLinks = defines["LINKS_DOMAIN"]
    if (definedLinks.isNullOrBlank() || definedLinks.contains(placeholderDomain)) {
        problems += "--dart-define=LINKS_DOMAIN is unset or the placeholder $placeholderDomain"
    } else if (links != null && definedLinks != links) {
        problems += "--dart-define=LINKS_DOMAIN ($definedLinks) differs from -PlinksDomain ($links)"
    }
    val api = defines["API_BASE_URL"]
    if (api.isNullOrBlank() || !api.startsWith("https://") || api.contains(placeholderDomain)) {
        problems += "--dart-define=API_BASE_URL must be the real https API origin"
    }
    for (key in listOf("APP_STORE_URL", "PLAY_STORE_URL")) {
        val v = defines[key]
        if (v != null && v.contains(placeholderDomain)) problems += "--dart-define=$key still holds $placeholderDomain"
    }
    return problems
}

// Runs only when a release variant is built: debug and profile builds and
// `flutter test` are unaffected.
gradle.taskGraph.whenReady {
    val buildsRelease = allTasks.any { it.project == project && it.name.contains("Release") }
    if (buildsRelease) {
        val problems = velaReleaseProblems()
        if (problems.isNotEmpty()) {
            throw GradleException(
                "Release build refused:\n" + problems.joinToString("\n") { " - $it" },
            )
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}
