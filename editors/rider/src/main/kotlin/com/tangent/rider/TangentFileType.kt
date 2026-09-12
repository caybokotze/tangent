package com.tangent.rider

import com.intellij.openapi.fileTypes.FileType
import com.intellij.openapi.util.IconLoader
import org.jetbrains.plugins.textmate.TextMateBackedFileType
import javax.swing.Icon

/** The `.tan` file extension, without the dot. */
internal const val TANGENT_EXTENSION: String = "tan"

internal object TangentIcons {
    /** 16x16 line-tangent-to-circle mark, shown in the project view and editor tabs. */
    val FILE: Icon = IconLoader.getIcon("/icons/tangent.svg", TangentIcons::class.java)
}

/**
 * File type for Tangent contract files.
 *
 * It implements [TextMateBackedFileType] on purpose. Without that marker, registering a
 * file type for `tan` would *shadow* the TextMate grammar: `TextMateFileType.isMyFileType`
 * only replaces file types that are unknown, plain text, or `TextMateBackedFileType`, so
 * a plain [FileType] here would leave `.tan` files unhighlighted.
 *
 * With the marker, this type supplies the name and the icon while the bundled TextMate
 * grammar (see [TangentTextMateBundleProvider]) still does the highlighting.
 */
object TangentFileType : FileType, TextMateBackedFileType {

    // A Kotlin `object` already exposes a static INSTANCE field, which is what
    // plugin.xml refers to via fileType fieldName="INSTANCE".

    override fun getName(): String = "Tangent"

    override fun getDescription(): String = "Tangent architectural contract"

    override fun getDefaultExtension(): String = TANGENT_EXTENSION

    override fun getIcon(): Icon = TangentIcons.FILE

    override fun isBinary(): Boolean = false

    override fun isReadOnly(): Boolean = false
}
