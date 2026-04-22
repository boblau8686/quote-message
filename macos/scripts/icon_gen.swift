#!/usr/bin/env swift
//
//  icon_gen.swift
//  Render the MsgDots app icon at a given pixel size and write a PNG.
//
//  Usage:  swift scripts/icon_gen.swift <size> <output.png>
//
//  Design:
//    * macOS squircle (rounded-rect, radius = 22.37% of side — Apple's own ratio)
//    * Red vertical gradient (top #F04C46 → bottom #B7272D)
//    * Faint white highlight band on the top half (glassy feel)
//    * Bold white "M" centered
//    * Two small white dots tucked into the upper-right (echoing message labels)
//
//  Pure CoreGraphics — no external deps.  Run via `swift` (interpreter); the
//  shebang above also makes it invokable as `./icon_gen.swift <size> <out>`.
//

import AppKit
import CoreGraphics
import Foundation

// -------- args ------------------------------------------------------------
guard CommandLine.arguments.count == 3,
      let size = Int(CommandLine.arguments[1]) else {
    FileHandle.standardError.write(Data("usage: icon_gen.swift <size> <output.png>\n".utf8))
    exit(2)
}
let outputPath = CommandLine.arguments[2]
let S = CGFloat(size)

// -------- context ---------------------------------------------------------
let cs = CGColorSpaceCreateDeviceRGB()
guard let ctx = CGContext(
    data: nil,
    width: size, height: size,
    bitsPerComponent: 8, bytesPerRow: 0,
    space: cs,
    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
) else {
    FileHandle.standardError.write(Data("failed to create bitmap context\n".utf8))
    exit(1)
}

// CoreGraphics origin is bottom-left — that's fine for the gradient (we map
// start=bottom end=top and reverse the color stops to get "top bright").

// -------- squircle clip ---------------------------------------------------
let radius = S * 0.2237   // Apple's ratio for app icon corner rounding
let rect = CGRect(x: 0, y: 0, width: S, height: S)
let squircle = CGPath(roundedRect: rect, cornerWidth: radius, cornerHeight: radius, transform: nil)

ctx.saveGState()
ctx.addPath(squircle)
ctx.clip()

// -------- red gradient (bottom-dark → top-bright) ------------------------
let topColor    = CGColor(srgbRed: 0xF0/255, green: 0x4C/255, blue: 0x46/255, alpha: 1)
let bottomColor = CGColor(srgbRed: 0xB7/255, green: 0x27/255, blue: 0x2D/255, alpha: 1)
let gradient = CGGradient(
    colorsSpace: cs,
    colors: [bottomColor, topColor] as CFArray,
    locations: [0, 1]
)!
ctx.drawLinearGradient(
    gradient,
    start: CGPoint(x: S/2, y: 0),
    end:   CGPoint(x: S/2, y: S),
    options: []
)

// -------- glossy highlight on the top half -------------------------------
// A very subtle brighter band across the top third, fading out to transparent.
let hi = CGGradient(
    colorsSpace: cs,
    colors: [
        CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 0.18),
        CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 0.00),
    ] as CFArray,
    locations: [0, 1]
)!
ctx.drawLinearGradient(
    hi,
    start: CGPoint(x: S/2, y: S),
    end:   CGPoint(x: S/2, y: S * 0.55),
    options: []
)

// -------- little message dots (top-right) --------------------------------
// Two filled circles, sized relative to S for resolution independence.
func drawMessageDot(cx: CGFloat, cy: CGFloat, d: CGFloat) {
    let r = CGRect(x: cx - d/2, y: cy - d/2, width: d, height: d)
    ctx.addEllipse(in: r)
}
ctx.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 0.82))
let dotD = S * 0.10
let dotY = S * 0.80    // up near the top
drawMessageDot(cx: S * 0.68, cy: dotY, d: dotD)
drawMessageDot(cx: S * 0.81, cy: dotY, d: dotD)
ctx.fillPath()

// -------- big white M (centered) -----------------------------------------
// Use NSAttributedString so we get proper font metrics and kerning.
// We draw via an NSGraphicsContext bridge — simplest way to get CoreText-
// quality text rendering on top of the CGContext.
let fontSize = S * 0.63
let font = NSFont.systemFont(ofSize: fontSize, weight: .heavy)
let attrs: [NSAttributedString.Key: Any] = [
    .font: font,
    .foregroundColor: NSColor.white,
    .kern: 0,
]
let glyph = NSAttributedString(string: "M", attributes: attrs)
let glyphSize = glyph.size()

let glyphOrigin = NSPoint(
    x: (S - glyphSize.width) / 2,
    y: (S - glyphSize.height) / 2 - S * 0.01
)

// Push AppKit drawing onto this CGContext.
let nsCtx = NSGraphicsContext(cgContext: ctx, flipped: false)
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = nsCtx
glyph.draw(at: glyphOrigin)
NSGraphicsContext.restoreGraphicsState()

ctx.restoreGState()

// -------- write PNG ------------------------------------------------------
guard let cgImage = ctx.makeImage() else {
    FileHandle.standardError.write(Data("failed to snapshot image\n".utf8))
    exit(1)
}
let rep = NSBitmapImageRep(cgImage: cgImage)
guard let png = rep.representation(using: .png, properties: [:]) else {
    FileHandle.standardError.write(Data("failed to encode PNG\n".utf8))
    exit(1)
}
let url = URL(fileURLWithPath: outputPath)
do {
    try png.write(to: url)
    print("wrote \(outputPath) (\(size)×\(size))")
} catch {
    FileHandle.standardError.write(Data("write failed: \(error)\n".utf8))
    exit(1)
}
