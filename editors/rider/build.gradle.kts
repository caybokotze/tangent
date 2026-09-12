import org.jetbrains.kotlin.gradle.dsl.JvmTarget
import org.jetbrains.kotlin.gradle.dsl.KotlinVersion

plugins {
    kotlin("jvm") version "2.0.21"
    id("org.jetbrains.intellij.platform") version "2.1.0"
}

group = providers.gradleProperty("pluginGroup").get()
version = providers.gradleProperty("pluginVersion").get()

repositories {
    mavenCentral()

    // Adds the JetBrains IntelliJ Platform repositories (releases, snapshots, marketplace).
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        // Equivalent to rider("2024.2"); the version lives in gradle.properties.
        rider(providers.gradleProperty("platformVersion"))

        // TextMate Bundles is bundled with every IntelliJ-based IDE, Rider included.
        // Needed to compile against org.jetbrains.plugins.textmate.* .
        bundledPlugin("org.jetbrains.plugins.textmate")
    }
}

kotlin {
    jvmToolchain(17)

    compilerOptions {
        jvmTarget = JvmTarget.JVM_17
        // Rider 2024.2 bundles Kotlin 1.9.x; compiling against a newer stdlib API
        // would break at runtime, so pin the language/API level.
        apiVersion = KotlinVersion.KOTLIN_1_9
        languageVersion = KotlinVersion.KOTLIN_1_9
        freeCompilerArgs.add("-Xjvm-default=all")
    }
}

intellijPlatform {
    buildSearchableOptions = false

    pluginConfiguration {
        id = "com.tangent.rider"
        name = "Tangent"
        version = providers.gradleProperty("pluginVersion")

        vendor {
            name = "Tangent"
            url = "https://github.com/tangent"
        }

        ideaVersion {
            sinceBuild = providers.gradleProperty("pluginSinceBuild")
            // Leave the upper bound open rather than pinning it to the build platform.
            untilBuild = provider { null }
        }
    }

    pluginVerification {
        ides {
            recommended()
        }
    }
}
