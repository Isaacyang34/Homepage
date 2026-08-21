$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add('http://127.0.0.1:8080/')
$listener.Prefixes.Add('http://localhost:8080/')
$listener.Start()
Write-Output 'Badminton Vision AI Server started on http://127.0.0.1:8080/'
$root = $PSScriptRoot
if ([string]::IsNullOrEmpty($root)) {
    $root = 'c:\Users\peter\OneDrive\Desktop\AI_Projects\WEB_Badminton_Player_Analysis'
}

while ($listener.IsListening) {
    try {
        $context = $listener.GetContext()
        $req = $context.Request
        $res = $context.Response
        
        # Add CORS headers
        $res.AddHeader('Access-Control-Allow-Origin', '*')
        $res.AddHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
        $res.AddHeader('Access-Control-Allow-Headers', '*')
        
        if ($req.HttpMethod -eq 'OPTIONS') {
            $res.StatusCode = 204
            $res.OutputStream.Close()
            continue
        }

        $localPath = $req.Url.LocalPath.TrimStart('/')
        if ([string]::IsNullOrEmpty($localPath)) { $localPath = 'index.html' }
        $filePath = Join-Path $root $localPath
        
        if (Test-Path $filePath -PathType Leaf) {
            $bytes = [System.IO.File]::ReadAllBytes($filePath)
            if ($filePath.EndsWith('.html')) { $res.ContentType = 'text/html; charset=utf-8' }
            elseif ($filePath.EndsWith('.js')) { $res.ContentType = 'application/javascript; charset=utf-8' }
            elseif ($filePath.EndsWith('.css')) { $res.ContentType = 'text/css; charset=utf-8' }
            elseif ($filePath.EndsWith('.mp4')) { $res.ContentType = 'video/mp4' }
            elseif ($filePath.EndsWith('.png')) { $res.ContentType = 'image/png' }
            elseif ($filePath.EndsWith('.json')) { $res.ContentType = 'application/json; charset=utf-8' }
            
            $res.ContentLength64 = $bytes.Length
            $res.OutputStream.Write($bytes, 0, $bytes.Length)
        } else {
            $res.StatusCode = 404
        }
        $res.OutputStream.Close()
    } catch {
        # continue listening on transient errors
    }
}
