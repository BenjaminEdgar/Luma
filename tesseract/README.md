# Tesseract OCR Binaries

This directory should contain the tesseract binaries and language data for cross-platform OCR support in Luma.

## Setup

Download tesseract binaries for your platform(s):

### Windows (x64)
```bash
# Download from: https://github.com/UB-Mannheim/tesseract/wiki
# Extract tesseract.exe and tessdata/ folder here
```

### macOS
```bash
# Using Homebrew
brew install tesseract

# Or download binary from: https://github.com/tesseract-ocr/tesseract
# Extract to this directory
```

### Linux
```bash
# Ubuntu/Debian
sudo apt-get install tesseract-ocr

# Or compile from source: https://github.com/tesseract-ocr/tesseract
# Extract to this directory
```

## Structure

```
tesseract/
  tesseract.exe         (Windows)
  tesseract            (macOS/Linux)
  tessdata/
    eng.traineddata    (English language data)
    [other languages]
```

## Notes

- Only `eng.traineddata` is required; download additional language files as needed
- Language data: https://github.com/tesseract-ocr/tessdata
- The app will automatically find bundled tesseract at runtime
