import { useState, useRef, type ReactElement, type ChangeEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  X,
  Loader2,
  AlertCircle,
  Upload,
  FileText,
  FileJson,
  CheckCircle2,
  XCircle
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import {
  MorphDBClient,
  type ImportJobResponse,
  type CsvImportOptions,
  type JsonImportOptions
} from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'

interface ImportDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  tableName: string
}

type ImportFormat = 'csv' | 'json' | 'ndjson'

export function ImportDialog({
  open,
  onOpenChange,
  tableName
}: ImportDialogProps): ReactElement | null {
  const { activeConnection } = useConnectionStore()
  const queryClient = useQueryClient()
  const fileInputRef = useRef<HTMLInputElement>(null)

  const [format, setFormat] = useState<ImportFormat>('csv')
  const [file, setFile] = useState<File | null>(null)
  const [jobResult, setJobResult] = useState<ImportJobResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  // CSV Options
  const [csvDelimiter, setCsvDelimiter] = useState(',')
  const [csvHasHeader, setCsvHasHeader] = useState(true)
  const [csvSkipRows, setCsvSkipRows] = useState(0)
  const [csvEncoding, setCsvEncoding] = useState('utf-8')

  // JSON Options
  const [jsonRootPath, setJsonRootPath] = useState('')
  const [jsonFlatten, setJsonFlatten] = useState(false)

  const createClient = async (): Promise<MorphDBClient | null> => {
    if (!activeConnection) return null
    return new MorphDBClient({
      url: activeConnection.url,
      projectId: activeConnection.projectId
    })
  }

  const importMutation = useMutation({
    mutationFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      if (!file) throw new Error('No file selected')

      if (format === 'csv') {
        const options: CsvImportOptions = {
          delimiter: csvDelimiter,
          hasHeader: csvHasHeader,
          skipRows: csvSkipRows,
          encoding: csvEncoding
        }
        return client.importCsv(tableName, file, options)
      } else {
        const options: JsonImportOptions = {
          rootPath: jsonRootPath || undefined,
          flattenNested: jsonFlatten
        }
        return client.importJson(tableName, file, options)
      }
    },
    onSuccess: (data) => {
      setJobResult(data)
      queryClient.invalidateQueries({ queryKey: ['table-data'] })
    },
    onError: (err) => {
      setError((err as Error).message)
    }
  })

  const handleFileChange = (e: ChangeEvent<HTMLInputElement>): void => {
    const selectedFile = e.target.files?.[0]
    if (selectedFile) {
      setFile(selectedFile)
      setJobResult(null)
      setError(null)

      // Auto-detect format from extension
      const ext = selectedFile.name.split('.').pop()?.toLowerCase()
      if (ext === 'csv') setFormat('csv')
      else if (ext === 'json') setFormat('json')
      else if (ext === 'ndjson' || ext === 'jsonl') setFormat('ndjson')
    }
  }

  const handleSubmit = async (): Promise<void> => {
    setError(null)
    setJobResult(null)

    if (!file) {
      setError('Please select a file to import')
      return
    }

    await importMutation.mutateAsync()
  }

  const handleClose = (): void => {
    onOpenChange(false)
    setTimeout(() => {
      setFile(null)
      setJobResult(null)
      setError(null)
      setFormat('csv')
    }, 200)
  }

  const selectFile = (): void => {
    fileInputRef.current?.click()
  }

  if (!open) return null

  const formatIcons = {
    csv: FileText,
    json: FileJson,
    ndjson: FileJson
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={handleClose} />
      <div className="relative z-50 w-full max-w-lg rounded-lg border bg-background p-6 shadow-lg max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <Upload className="h-5 w-5 text-primary" />
            <h2 className="text-lg font-semibold">Import to: {tableName}</h2>
          </div>
          <Button variant="ghost" size="icon" onClick={handleClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Format Selection */}
        <div className="mb-4">
          <label className="block text-sm font-medium mb-2">Format</label>
          <div className="flex gap-2">
            {(['csv', 'json', 'ndjson'] as ImportFormat[]).map((f) => {
              const Icon = formatIcons[f]
              return (
                <button
                  key={f}
                  onClick={() => setFormat(f)}
                  className={cn(
                    'flex items-center gap-2 px-3 py-2 rounded border text-sm transition-colors',
                    format === f
                      ? 'bg-primary text-primary-foreground border-primary'
                      : 'bg-background border-border hover:border-primary'
                  )}
                >
                  <Icon className="h-4 w-4" />
                  {f.toUpperCase()}
                </button>
              )
            })}
          </div>
        </div>

        {/* File Selection */}
        <div className="mb-4">
          <label className="block text-sm font-medium mb-2">File</label>
          <input
            ref={fileInputRef}
            type="file"
            accept={format === 'csv' ? '.csv' : '.json,.ndjson,.jsonl'}
            onChange={handleFileChange}
            className="hidden"
          />
          <div
            onClick={selectFile}
            className={cn(
              'border-2 border-dashed rounded-lg p-6 text-center cursor-pointer transition-colors',
              file ? 'border-primary bg-primary/5' : 'border-border hover:border-primary'
            )}
          >
            {file ? (
              <div className="flex items-center justify-center gap-2">
                <FileText className="h-5 w-5 text-primary" />
                <span className="text-sm">{file.name}</span>
                <span className="text-xs text-muted-foreground">
                  ({(file.size / 1024).toFixed(1)} KB)
                </span>
              </div>
            ) : (
              <div>
                <Upload className="h-8 w-8 mx-auto mb-2 text-muted-foreground" />
                <p className="text-sm text-muted-foreground">
                  Click to select or drag and drop
                </p>
              </div>
            )}
          </div>
        </div>

        {/* CSV Options */}
        {format === 'csv' && (
          <div className="mb-4 space-y-3 p-3 rounded bg-muted/30">
            <h3 className="text-sm font-medium">CSV Options</h3>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="text-xs text-muted-foreground">Delimiter</label>
                <select
                  value={csvDelimiter}
                  onChange={(e) => setCsvDelimiter(e.target.value)}
                  className="w-full h-8 rounded border bg-background px-2 text-sm"
                >
                  <option value=",">Comma (,)</option>
                  <option value=";">Semicolon (;)</option>
                  <option value="\t">Tab</option>
                  <option value="|">Pipe (|)</option>
                </select>
              </div>
              <div>
                <label className="text-xs text-muted-foreground">Encoding</label>
                <select
                  value={csvEncoding}
                  onChange={(e) => setCsvEncoding(e.target.value)}
                  className="w-full h-8 rounded border bg-background px-2 text-sm"
                >
                  <option value="utf-8">UTF-8</option>
                  <option value="latin1">Latin-1</option>
                  <option value="ascii">ASCII</option>
                </select>
              </div>
              <div>
                <label className="text-xs text-muted-foreground">Skip Rows</label>
                <input
                  type="number"
                  value={csvSkipRows}
                  onChange={(e) => setCsvSkipRows(parseInt(e.target.value) || 0)}
                  min={0}
                  className="w-full h-8 rounded border bg-background px-2 text-sm"
                />
              </div>
              <div className="flex items-end">
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={csvHasHeader}
                    onChange={(e) => setCsvHasHeader(e.target.checked)}
                    className="rounded"
                  />
                  Has header row
                </label>
              </div>
            </div>
          </div>
        )}

        {/* JSON Options */}
        {(format === 'json' || format === 'ndjson') && (
          <div className="mb-4 space-y-3 p-3 rounded bg-muted/30">
            <h3 className="text-sm font-medium">JSON Options</h3>
            <div className="space-y-3">
              <div>
                <label className="text-xs text-muted-foreground">
                  Root Path (for nested arrays)
                </label>
                <input
                  type="text"
                  value={jsonRootPath}
                  onChange={(e) => setJsonRootPath(e.target.value)}
                  placeholder="e.g., data.items"
                  className="w-full h-8 rounded border bg-background px-2 text-sm"
                />
              </div>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={jsonFlatten}
                  onChange={(e) => setJsonFlatten(e.target.checked)}
                  className="rounded"
                />
                Flatten nested objects
              </label>
            </div>
          </div>
        )}

        {/* Error */}
        {error && (
          <div className="flex items-center gap-2 p-3 mb-4 rounded bg-destructive/10 text-destructive text-sm">
            <AlertCircle className="h-4 w-4" />
            {error}
          </div>
        )}

        {/* Job Result */}
        {jobResult && (
          <div className={cn(
            'p-3 mb-4 rounded text-sm',
            jobResult.status === 'completed' ? 'bg-success/10' : 'bg-muted'
          )}>
            <div className="flex items-center gap-2 mb-2">
              {jobResult.status === 'completed' ? (
                <CheckCircle2 className="h-4 w-4 text-success" />
              ) : jobResult.status === 'failed' ? (
                <XCircle className="h-4 w-4 text-destructive" />
              ) : (
                <Loader2 className="h-4 w-4 animate-spin" />
              )}
              <span className="font-medium capitalize">{jobResult.status}</span>
            </div>
            <div className="grid grid-cols-3 gap-2 text-xs text-muted-foreground">
              <div>
                <span className="block font-medium text-foreground">
                  {jobResult.processedRows}
                </span>
                Processed
              </div>
              <div>
                <span className="block font-medium text-success">
                  {jobResult.successCount}
                </span>
                Success
              </div>
              <div>
                <span className="block font-medium text-destructive">
                  {jobResult.errorCount}
                </span>
                Errors
              </div>
            </div>
            {jobResult.errors && jobResult.errors.length > 0 && (
              <div className="mt-2 text-xs text-destructive">
                {jobResult.errors.slice(0, 3).map((err, i) => (
                  <div key={i}>Row {err.row}: {err.message}</div>
                ))}
                {jobResult.errors.length > 3 && (
                  <div>...and {jobResult.errors.length - 3} more errors</div>
                )}
              </div>
            )}
          </div>
        )}

        {/* Actions */}
        <div className="flex justify-end gap-2">
          <Button variant="outline" onClick={handleClose}>
            {jobResult ? 'Close' : 'Cancel'}
          </Button>
          {!jobResult && (
            <Button
              onClick={handleSubmit}
              disabled={!file || importMutation.isPending}
            >
              {importMutation.isPending ? (
                <Loader2 className="h-4 w-4 mr-2 animate-spin" />
              ) : (
                <Upload className="h-4 w-4 mr-2" />
              )}
              Import
            </Button>
          )}
        </div>
      </div>
    </div>
  )
}
