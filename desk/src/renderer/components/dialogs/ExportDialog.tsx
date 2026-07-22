import { useState, type ReactElement } from 'react'
import { useMutation } from '@tanstack/react-query'
import {
  X,
  Loader2,
  AlertCircle,
  Download,
  FileText,
  FileJson,
  FileSpreadsheet,
  CheckCircle2
} from 'lucide-react'
import { Button } from '@/components/ui/Button'
import {
  MorphDBClient,
  type ColumnApiResponse,
  type ExportJobResponse,
  type CsvExportOptions,
  type JsonExportOptions,
  type XlsxExportOptions
} from '@/lib/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { cn } from '@/lib/utils'

interface ExportDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  tableName: string
  columns: ColumnApiResponse[]
}

type ExportFormat = 'csv' | 'json' | 'xlsx'

export function ExportDialog({
  open,
  onOpenChange,
  tableName,
  columns
}: ExportDialogProps): ReactElement | null {
  const { activeConnection } = useConnectionStore()

  const [format, setFormat] = useState<ExportFormat>('csv')
  const [selectedColumns, setSelectedColumns] = useState<string[]>([])
  const [filter, setFilter] = useState('')
  const [limit, setLimit] = useState<number | undefined>(undefined)
  const [jobResult, setJobResult] = useState<ExportJobResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  // CSV Options
  const [csvDelimiter, setCsvDelimiter] = useState(',')
  const [csvIncludeHeader, setCsvIncludeHeader] = useState(true)

  // JSON Options
  const [jsonPretty, setJsonPretty] = useState(false)
  const [jsonArrayFormat, setJsonArrayFormat] = useState(true)

  // XLSX Options
  const [xlsxSheetName, setXlsxSheetName] = useState('')

  const exportableColumns = columns.filter(c => !c.name.startsWith('_') || c.name === '_id')

  const createClient = async (): Promise<MorphDBClient | null> => {
    if (!activeConnection) return null
    return new MorphDBClient({
      url: activeConnection.url,
      projectId: activeConnection.projectId
    })
  }

  const exportMutation = useMutation({
    mutationFn: async () => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')

      const cols = selectedColumns.length > 0 ? selectedColumns : undefined
      const filterStr = filter.trim() || undefined

      if (format === 'csv') {
        const options: CsvExportOptions = {
          columns: cols,
          filter: filterStr,
          limit,
          delimiter: csvDelimiter,
          includeHeader: csvIncludeHeader
        }
        return client.exportCsv(tableName, options)
      } else if (format === 'json') {
        const options: JsonExportOptions = {
          columns: cols,
          filter: filterStr,
          limit,
          pretty: jsonPretty,
          arrayFormat: jsonArrayFormat
        }
        return client.exportJson(tableName, options)
      } else {
        const options: XlsxExportOptions = {
          columns: cols,
          filter: filterStr,
          limit,
          sheetName: xlsxSheetName || tableName
        }
        return client.exportXlsx(tableName, options)
      }
    },
    onSuccess: (data) => {
      setJobResult(data)
    },
    onError: (err) => {
      setError((err as Error).message)
    }
  })

  const downloadMutation = useMutation({
    mutationFn: async (jobId: string) => {
      const client = await createClient()
      if (!client) throw new Error('No active connection')
      return client.downloadExport(jobId)
    },
    onSuccess: (blob) => {
      const ext = format === 'xlsx' ? 'xlsx' : format
      const filename = `${tableName}_export.${ext}`
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
    },
    onError: (err) => {
      setError((err as Error).message)
    }
  })

  const toggleColumn = (columnName: string): void => {
    setSelectedColumns(prev =>
      prev.includes(columnName)
        ? prev.filter(c => c !== columnName)
        : [...prev, columnName]
    )
  }

  const selectAllColumns = (): void => {
    setSelectedColumns(exportableColumns.map(c => c.name))
  }

  const clearColumns = (): void => {
    setSelectedColumns([])
  }

  const handleSubmit = async (): Promise<void> => {
    setError(null)
    setJobResult(null)
    await exportMutation.mutateAsync()
  }

  const handleDownload = async (): Promise<void> => {
    if (!jobResult) return
    await downloadMutation.mutateAsync(jobResult.jobId)
  }

  const handleClose = (): void => {
    onOpenChange(false)
    setTimeout(() => {
      setFormat('csv')
      setSelectedColumns([])
      setFilter('')
      setLimit(undefined)
      setJobResult(null)
      setError(null)
    }, 200)
  }

  if (!open) return null

  const formatIcons = {
    csv: FileText,
    json: FileJson,
    xlsx: FileSpreadsheet
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/50" onClick={handleClose} />
      <div className="relative z-50 w-full max-w-lg rounded-lg border bg-background p-6 shadow-lg max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <Download className="h-5 w-5 text-primary" />
            <h2 className="text-lg font-semibold">Export: {tableName}</h2>
          </div>
          <Button variant="ghost" size="icon" onClick={handleClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Format Selection */}
        <div className="mb-4">
          <label className="block text-sm font-medium mb-2">Format</label>
          <div className="flex gap-2">
            {(['csv', 'json', 'xlsx'] as ExportFormat[]).map((f) => {
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

        {/* Column Selection */}
        <div className="mb-4">
          <div className="flex items-center justify-between mb-2">
            <label className="text-sm font-medium">
              Columns ({selectedColumns.length || 'All'})
            </label>
            <div className="flex gap-2">
              <button
                onClick={selectAllColumns}
                className="text-xs text-primary hover:underline"
              >
                Select All
              </button>
              <button
                onClick={clearColumns}
                className="text-xs text-muted-foreground hover:underline"
              >
                Clear
              </button>
            </div>
          </div>
          <div className="flex flex-wrap gap-2 max-h-32 overflow-y-auto p-2 border rounded">
            {exportableColumns.map((col) => (
              <button
                key={col.name}
                onClick={() => toggleColumn(col.name)}
                className={cn(
                  'px-2 py-1 rounded text-xs border transition-colors',
                  selectedColumns.includes(col.name)
                    ? 'bg-primary text-primary-foreground border-primary'
                    : 'bg-background border-border hover:border-primary'
                )}
              >
                {col.displayName || col.name}
              </button>
            ))}
          </div>
          <p className="text-xs text-muted-foreground mt-1">
            Leave empty to export all columns
          </p>
        </div>

        {/* Filter */}
        <div className="mb-4">
          <label className="block text-sm font-medium mb-1">
            Filter (optional)
          </label>
          <input
            type="text"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="e.g., status:eq:active"
            className="w-full rounded-md border bg-background px-3 py-2 text-sm"
          />
        </div>

        {/* Limit */}
        <div className="mb-4">
          <label className="block text-sm font-medium mb-1">
            Row Limit (optional)
          </label>
          <input
            type="number"
            value={limit || ''}
            onChange={(e) => setLimit(e.target.value ? parseInt(e.target.value) : undefined)}
            placeholder="Leave empty for all rows"
            min={1}
            className="w-full rounded-md border bg-background px-3 py-2 text-sm"
          />
        </div>

        {/* Format-specific Options */}
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
              <div className="flex items-end">
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={csvIncludeHeader}
                    onChange={(e) => setCsvIncludeHeader(e.target.checked)}
                    className="rounded"
                  />
                  Include header
                </label>
              </div>
            </div>
          </div>
        )}

        {format === 'json' && (
          <div className="mb-4 space-y-3 p-3 rounded bg-muted/30">
            <h3 className="text-sm font-medium">JSON Options</h3>
            <div className="space-y-2">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={jsonPretty}
                  onChange={(e) => setJsonPretty(e.target.checked)}
                  className="rounded"
                />
                Pretty print (formatted)
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={jsonArrayFormat}
                  onChange={(e) => setJsonArrayFormat(e.target.checked)}
                  className="rounded"
                />
                Array format (vs. NDJSON)
              </label>
            </div>
          </div>
        )}

        {format === 'xlsx' && (
          <div className="mb-4 space-y-3 p-3 rounded bg-muted/30">
            <h3 className="text-sm font-medium">Excel Options</h3>
            <div>
              <label className="text-xs text-muted-foreground">Sheet Name</label>
              <input
                type="text"
                value={xlsxSheetName}
                onChange={(e) => setXlsxSheetName(e.target.value)}
                placeholder={tableName}
                className="w-full h-8 rounded border bg-background px-2 text-sm"
              />
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
          <div className="p-3 mb-4 rounded bg-success/10 text-sm">
            <div className="flex items-center gap-2 mb-2">
              <CheckCircle2 className="h-4 w-4 text-success" />
              <span className="font-medium">Export Ready</span>
            </div>
            <div className="grid grid-cols-2 gap-2 text-xs text-muted-foreground">
              <div>
                <span className="block font-medium text-foreground">
                  {jobResult.rowCount?.toLocaleString() || 0}
                </span>
                Rows
              </div>
              <div>
                <span className="block font-medium text-foreground">
                  {jobResult.fileSize ? `${(jobResult.fileSize / 1024).toFixed(1)} KB` : '-'}
                </span>
                File Size
              </div>
            </div>
          </div>
        )}

        {/* Actions */}
        <div className="flex justify-end gap-2">
          <Button variant="outline" onClick={handleClose}>
            {jobResult ? 'Close' : 'Cancel'}
          </Button>
          {!jobResult ? (
            <Button
              onClick={handleSubmit}
              disabled={exportMutation.isPending}
            >
              {exportMutation.isPending ? (
                <Loader2 className="h-4 w-4 mr-2 animate-spin" />
              ) : (
                <Download className="h-4 w-4 mr-2" />
              )}
              Export
            </Button>
          ) : (
            <Button
              onClick={handleDownload}
              disabled={downloadMutation.isPending}
            >
              {downloadMutation.isPending ? (
                <Loader2 className="h-4 w-4 mr-2 animate-spin" />
              ) : (
                <Download className="h-4 w-4 mr-2" />
              )}
              Download
            </Button>
          )}
        </div>
      </div>
    </div>
  )
}
