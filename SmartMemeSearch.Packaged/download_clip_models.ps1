$ErrorActionPreference = "Stop"

$commit = "main"
$repo = "Xenova/clip-vit-base-patch32"

function Download($file, $target) {
    $url = "https://huggingface.co/$repo/resolve/$commit/$file"
    Write-Host "Downloading $url ..."
    Invoke-WebRequest -Uri $url -OutFile $target
}

md Assets -Force

Download "onnx/text_model.onnx"   "Assets/clip_text.onnx"
Download "onnx/vision_model.onnx" "Assets/clip_image.onnx"
Download "tokenizer.json"         "Assets/tokenizer.json"

Write-Host "`nDownloaded models:"
Get-ChildItem Assets
