import { useEffect, useRef, useState, type DragEvent, type FormEvent } from 'react'
import {
  getAutomaticCadesDevelopmentDefaults,
  getAutomaticPadesDevelopmentDefaults,
  getSigningOptions,
  signAutomaticCades,
  signAutomaticPades,
  type SigningOptions,
} from './api/signingClient'

type CertificateType = 'AUTOMATIC' | 'REMOTE'
type SignatureType = 'CAdES' | 'PAdES' | 'XAdES' | 'HASH'

const signatureLevelChoices = [
  { value: 'BASELINE-B', enabled: true },
  { value: 'BASELINE-T', enabled: false },
  { value: 'BASELINE-LT', enabled: false },
  { value: 'BASELINE-LTV', enabled: false },
]

const certificateTypes: { value: CertificateType; label: string; enabled: boolean }[] = [
  { value: 'AUTOMATIC', label: 'Automatic signature', enabled: true },
  { value: 'REMOTE', label: 'Remote signature', enabled: false },
]

const signatureTypes: { value: SignatureType; label: string; enabled: boolean }[] = [
  { value: 'CAdES', label: 'CAdES', enabled: true },
  { value: 'PAdES', label: 'PAdES', enabled: true },
  { value: 'XAdES', label: 'XAdES', enabled: false },
  { value: 'HASH', label: 'Hash', enabled: false },
]

type SigningResult = {
  originalFileName: string
  downloadFileName: string
  downloadUrl: string
  requestId: string | null
}

function formatFileSize(size: number): string {
  if (size < 1024 * 1024) {
    return `${(size / 1024).toFixed(1)} KB`
  }

  return `${(size / (1024 * 1024)).toFixed(2)} MB`
}

function detectedContentType(file: File, options: SigningOptions): string {
  if (file.type) {
    return file.type
  }

  const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase()
  return options.allowedExtensions.includes(extension)
    ? options.allowedContentTypes[0] ?? 'Unknown'
    : 'Unknown'
}

function SigningPage() {
  const fileInput = useRef<HTMLInputElement>(null)
  const submitting = useRef(false)
  const editedFields = useRef(new Set<string>())
  const [options, setOptions] = useState<SigningOptions | null>(null)
  const [optionsError, setOptionsError] = useState<string | null>(null)
  const [defaultsError, setDefaultsError] = useState<string | null>(null)
  const [certificateType, setCertificateType] =
    useState<CertificateType>('AUTOMATIC')
  const [signatureType, setSignatureType] = useState<SignatureType>('CAdES')
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState<string | null>(null)
  const [certificateId, setCertificateId] = useState('')
  const [xSignerId, setXSignerId] = useState('')
  const [pin, setPin] = useState('')
  const [sat, setSat] = useState('')
  const [packaging, setPackaging] = useState('')
  const [signatureLevel, setSignatureLevel] = useState('BASELINE-B')
  const [isVisible, setIsVisible] = useState(true)
  const [isPdfa, setIsPdfa] = useState(true)
  const [page, setPage] = useState(1)
  const [llx, setLlx] = useState(100)
  const [lly, setLly] = useState(100)
  const [urx, setUrx] = useState(20)
  const [ury, setUry] = useState(20)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<SigningResult | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void getSigningOptions(controller.signal)
      .then(setOptions)
      .catch((loadError: unknown) => {
        if (!controller.signal.aborted) {
          setOptionsError(
            loadError instanceof Error
              ? loadError.message
              : 'Signing options are unavailable.',
          )
        }
      })

    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (!import.meta.env.DEV || certificateType !== 'AUTOMATIC') {
      return
    }

    const controller = new AbortController()
    void getAutomaticCadesDevelopmentDefaults(controller.signal)
      .then((defaults) => {
        if (!editedFields.current.has('certificateId')) {
          setCertificateId(defaults.certificateId)
        }
        if (!editedFields.current.has('xSignerId')) {
          setXSignerId(defaults.xSignerId)
        }
        if (!editedFields.current.has('pin')) {
          setPin(defaults.pin)
        }
        if (!editedFields.current.has('sat')) {
          setSat(defaults.infocertSat)
        }
        if (!editedFields.current.has('packaging')) {
          setPackaging(
            ['ENVELOPED', 'ENVELOPING'].includes(defaults.packaging)
              ? defaults.packaging
              : '',
          )
        }
        if (!editedFields.current.has('signatureLevel')) {
          setSignatureLevel(
            defaults.signatureLevel === 'BASELINE-B'
              ? defaults.signatureLevel
              : 'BASELINE-B',
          )
        }
        setDefaultsError(null)
      })
      .catch((loadError: unknown) => {
        if (!controller.signal.aborted) {
          setDefaultsError(
            loadError instanceof Error
              ? loadError.message
              : 'Development signing defaults are unavailable.',
          )
        }
      })

    return () => controller.abort()
  }, [certificateType])

  useEffect(() => {
    if (
      !import.meta.env.DEV ||
      certificateType !== 'AUTOMATIC' ||
      signatureType !== 'PAdES'
    ) {
      return
    }

    const controller = new AbortController()
    void getAutomaticPadesDevelopmentDefaults(controller.signal)
      .then((defaults) => {
        if (!editedFields.current.has('isVisible')) setIsVisible(defaults.isVisible)
        if (!editedFields.current.has('isPdfa')) setIsPdfa(defaults.isPdfa)
        if (!editedFields.current.has('page')) setPage(defaults.page)
        if (!editedFields.current.has('llx')) setLlx(defaults.llx)
        if (!editedFields.current.has('lly')) setLly(defaults.lly)
        if (!editedFields.current.has('urx')) setUrx(defaults.urx)
        if (!editedFields.current.has('ury')) setUry(defaults.ury)
        setDefaultsError(null)
      })
      .catch((loadError: unknown) => {
        if (!controller.signal.aborted) {
          setDefaultsError(
            loadError instanceof Error
              ? loadError.message
              : 'Development PAdES signing defaults are unavailable.',
          )
        }
      })

    return () => controller.abort()
  }, [certificateType, signatureType])

  useEffect(
    () => () => {
      if (result) {
        URL.revokeObjectURL(result.downloadUrl)
      }
    },
    [result],
  )

  const chooseFile = (selectedFiles: FileList | File[]) => {
    setError(null)
    setResult(null)
    if (selectedFiles.length !== 1) {
      setFileError('Select exactly one PDF file.')
      return
    }

    const selectedFile = selectedFiles[0]
    if (!selectedFile || !options) {
      setFileError('Signing options are not available.')
      return
    }

    const extension = selectedFile.name
      .slice(selectedFile.name.lastIndexOf('.'))
      .toLowerCase()
    if (
      !options.allowedExtensions.includes(extension) ||
      (selectedFile.type !== '' &&
        !options.allowedContentTypes.includes(selectedFile.type))
    ) {
      setFileError('Only PDF documents are supported.')
      return
    }

    if (selectedFile.size === 0) {
      setFileError('The selected file is empty.')
      return
    }

    if (selectedFile.size > options.maxFileSizeBytes) {
      setFileError(
        `The file exceeds the ${formatFileSize(options.maxFileSizeBytes)} TrustPlatform upload limit.`,
      )
      return
    }

    setFileError(null)
    setFile(selectedFile)
  }

  const handleDrop = (event: DragEvent<HTMLElement>) => {
    event.preventDefault()
    if (event.dataTransfer.files.length > 0) {
      chooseFile(event.dataTransfer.files)
    }
  }

  const isSupported =
    certificateType === 'AUTOMATIC' &&
    (signatureType === 'CAdES' || signatureType === 'PAdES')
  const markEdited = (field: string) => {
    editedFields.current.add(field)
  }
  const canSubmit =
    options !== null &&
    isSupported &&
    file !== null &&
    fileError === null &&
    certificateId.trim() !== '' &&
    xSignerId.trim() !== '' &&
    pin !== '' &&
    sat.trim() !== '' &&
    packaging !== '' &&
    signatureLevel === 'BASELINE-B' &&
    options.signatureLevelValues.includes('BASELINE-B') &&
    (signatureType !== 'PAdES' ||
      (isVisible === true
        ? isPdfa !== null &&
          Number.isInteger(page) &&
          page >= 1 &&
          Number.isInteger(llx) &&
          Number.isInteger(lly) &&
          Number.isInteger(urx) &&
          Number.isInteger(ury)
        : true)) &&
    !isSubmitting

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!canSubmit || !file || submitting.current) {
      return
    }

    submitting.current = true
    setIsSubmitting(true)
    setError(null)
    setResult(null)
    const originalFileName = file.name
    const formData = new FormData()
    formData.append('file', file)
    formData.append('certificateId', certificateId.trim())
    formData.append('xSignerId', xSignerId.trim())
    formData.append('pin', pin)
    formData.append('sat', sat.trim())
    formData.append('packaging', packaging)
    formData.append('signatureLevel', signatureLevel)

    try {
      if (signatureType === 'PAdES') {
        formData.append('isVisible', isVisible ? 'true' : 'false')
        if (isVisible) {
          formData.append('isPdfa', isPdfa ? 'true' : 'false')
          formData.append('page', page.toString())
          formData.append('llx', llx.toString())
          formData.append('lly', lly.toString())
          formData.append('urx', urx.toString())
          formData.append('ury', ury.toString())
        }
      }

      const signed = signatureType === 'PAdES'
        ? await signAutomaticPades(formData)
        : await signAutomaticCades(formData)
      const downloadUrl = URL.createObjectURL(signed.blob)
      setResult({
        originalFileName,
        downloadFileName: signed.fileName,
        downloadUrl,
        requestId: signed.requestId,
      })
      setFile(null)
      if (fileInput.current) {
        fileInput.current.value = ''
      }
    } catch (signingError: unknown) {
      setError(
        signingError instanceof Error
          ? signingError.message
          : 'The signing request could not be completed.',
      )
    } finally {
      setPin('')
      setSat('')
      setIsSubmitting(false)
      submitting.current = false
    }
  }

  return (
    <section aria-labelledby="signing-heading">
      <p className="eyebrow">Workspace</p>
      <h1 id="signing-heading">Signing</h1>
      <p className="lead">
        Sign one PDF document using the supported automatic CAdES workflow.
      </p>

      <form className="signing-form" onSubmit={(event) => void submit(event)}>
        <div className="form-field">
          <label htmlFor="certificate-type">Certificate type</label>
          <select
            id="certificate-type"
            onChange={(event) =>
              setCertificateType(event.target.value as CertificateType)
            }
            value={certificateType}
          >
            {certificateTypes.map((option) => (
              <option
                disabled={!option.enabled}
                key={option.value}
                value={option.value}
              >
                {option.label}
                {!option.enabled ? ' — Not implemented yet' : ''}
              </option>
            ))}
          </select>
        </div>

        <div className="form-field">
          <label htmlFor="signature-type">Signature type</label>
          <select
            id="signature-type"
            onChange={(event) =>
              setSignatureType(event.target.value as SignatureType)
            }
            value={signatureType}
          >
            {signatureTypes.map((option) => (
              <option
                disabled={!option.enabled}
                key={option.value}
                value={option.value}
              >
                {option.label}
                {!option.enabled ? ' — Not implemented yet' : ''}
              </option>
            ))}
          </select>
        </div>

        {certificateType === 'REMOTE' && (
          <p className="unsupported-notice">Remote signature — Not implemented yet</p>
        )}
        {signatureType !== 'CAdES' && signatureType !== 'PAdES' && (
          <p className="unsupported-notice">
            {signatureType} — Not implemented yet
          </p>
        )}

        <div
          aria-describedby={fileError ? 'file-error' : undefined}
          aria-label="PDF document upload"
          className="file-dropzone"
          onDragOver={(event) => event.preventDefault()}
          onDrop={handleDrop}
        >
          <p className="file-dropzone-title">Drag and drop one PDF here</p>
          <p className="file-dropzone-help">
            Maximum size: {options ? formatFileSize(options.maxFileSizeBytes) : 'Loading…'}
          </p>
          <input
            accept={[
              ...(options?.allowedExtensions ?? []),
              ...(options?.allowedContentTypes ?? []),
            ].join(',')}
            aria-label="Select a PDF document"
            className="visually-hidden-file-input"
            id="signing-file"
            onChange={(event) => {
              if (event.currentTarget.files) {
                chooseFile(event.currentTarget.files)
                event.currentTarget.value = ''
              }
            }}
            ref={fileInput}
            tabIndex={-1}
            type="file"
          />
          <button
            className="secondary-button"
            onClick={() => fileInput.current?.click()}
            type="button"
          >
            {file ? 'Replace file' : 'Select file'}
          </button>
          {file && options && (
            <dl className="selected-file" aria-label="Selected document">
              <div>
                <dt>Filename:</dt>
                <dd>{file.name}</dd>
              </div>
              <div>
                <dt>Size:</dt>
                <dd>{formatFileSize(file.size)}</dd>
              </div>
              <div>
                <dt>Detected content type:</dt>
                <dd>{detectedContentType(file, options)}</dd>
              </div>
            </dl>
          )}
          {file && (
            <button
              className="text-button"
              onClick={() => {
                setFile(null)
                setFileError(null)
                setError(null)
                setResult(null)
                if (fileInput.current) {
                  fileInput.current.value = ''
                }
              }}
              type="button"
            >
              Remove file
            </button>
          )}
          {fileError && (
            <p className="field-error" id="file-error" role="alert">
              {fileError}
            </p>
          )}
        </div>

        <div className="form-field">
          <label htmlFor="certificate-id">Certificate ID</label>
          <input
            autoComplete="off"
            aria-describedby="certificate-id-help"
            id="certificate-id"
            onChange={(event) => {
              markEdited('certificateId')
              setCertificateId(event.target.value)
            }}
            required
            value={certificateId}
          />
          <p id="certificate-id-help">
            Certificate identifier used to build the SIGN API endpoint.
          </p>
        </div>

        <div className="form-field">
          <label htmlFor="x-signer-id">X-signer-id</label>
          <input
            autoComplete="off"
            aria-describedby="x-signer-id-help"
            id="x-signer-id"
            onChange={(event) => {
              markEdited('xSignerId')
              setXSignerId(event.target.value)
            }}
            required
            value={xSignerId}
          />
          <p id="x-signer-id-help">
            Signing identifier sent in the X-signer-id provider header.
          </p>
        </div>

        <div className="form-field">
          <label htmlFor="signing-pin">PIN</label>
          <input
            autoComplete="new-password"
            aria-describedby="signing-pin-help"
            id="signing-pin"
            onChange={(event) => {
              markEdited('pin')
              setPin(event.target.value)
            }}
            required
            type="password"
            value={pin}
          />
          <p id="signing-pin-help">
            PIN used by the automatic signing operation.
          </p>
        </div>

        <div className="form-field">
          <label htmlFor="infocert-sat">Infocert-SAT</label>
          <input
            autoComplete="new-password"
            aria-describedby="infocert-sat-help"
            id="infocert-sat"
            onChange={(event) => {
              markEdited('sat')
              setSat(event.target.value)
            }}
            required
            type="password"
            value={sat}
          />
          <p id="infocert-sat-help">
            SAT credential used by the automatic signing operation.
          </p>
        </div>

        <div className="form-field">
          <label htmlFor="packaging">Packaging</label>
          <select
            id="packaging"
            onChange={(event) => {
              markEdited('packaging')
              setPackaging(event.target.value)
            }}
            required
            value={packaging}
          >
            <option value="">Select packaging</option>
            {(options?.packagingValues ?? []).map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </div>

        <div className="form-field">
          <label htmlFor="signature-level">Signature level</label>
          <select
            id="signature-level"
            onChange={(event) => {
              markEdited('signatureLevel')
              setSignatureLevel(event.target.value)
            }}
            required
            value={signatureLevel}
          >
            <option value="">Select signature level</option>
            {signatureLevelChoices.map(({ value, enabled }) => (
              <option
                disabled={!enabled || !options?.signatureLevelValues.includes(value)}
                key={value}
                value={value}
              >
                {value}{!enabled ? ' — Not implemented' : ''}
              </option>
            ))}
          </select>
        </div>

        {signatureType === 'PAdES' && (
          <fieldset className="pades-appearance">
            <legend>PAdES signature appearance</legend>
            <div className="form-field">
              <label htmlFor="pades-visible">Visible signature</label>
              <select
                id="pades-visible"
                onChange={(event) => {
                  markEdited('isVisible')
                  setIsVisible(event.target.value === 'true')
                }}
                value={isVisible ? 'true' : 'false'}
              >
                <option value="true">true</option>
                <option value="false">false</option>
              </select>
            </div>

            {isVisible && (
              <>
                <div className="form-field">
                  <label htmlFor="pades-pdfa">PDF/A</label>
                  <select
                    id="pades-pdfa"
                    onChange={(event) => {
                      markEdited('isPdfa')
                      setIsPdfa(event.target.value === 'true')
                    }}
                    value={isPdfa ? 'true' : 'false'}
                  >
                    <option value="true">true</option>
                    <option value="false">false</option>
                  </select>
                </div>
                <div className="form-field">
                  <label htmlFor="pades-page">Page</label>
                  <input
                    id="pades-page"
                    min={1}
                    onChange={(event) => {
                      markEdited('page')
                      setPage(event.target.valueAsNumber)
                    }}
                    required
                    type="number"
                    value={Number.isFinite(page) ? page : ''}
                  />
                </div>
                <div className="form-field">
                  <label htmlFor="pades-llx">Lower-left X (llx)</label>
                  <input
                    id="pades-llx"
                    onChange={(event) => {
                      markEdited('llx')
                      setLlx(event.target.valueAsNumber)
                    }}
                    required
                    type="number"
                    value={Number.isFinite(llx) ? llx : ''}
                  />
                </div>
                <div className="form-field">
                  <label htmlFor="pades-lly">Lower-left Y (lly)</label>
                  <input
                    id="pades-lly"
                    onChange={(event) => {
                      markEdited('lly')
                      setLly(event.target.valueAsNumber)
                    }}
                    required
                    type="number"
                    value={Number.isFinite(lly) ? lly : ''}
                  />
                </div>
                <div className="form-field">
                  <label htmlFor="pades-urx">Upper-right X (urx)</label>
                  <input
                    id="pades-urx"
                    onChange={(event) => {
                      markEdited('urx')
                      setUrx(event.target.valueAsNumber)
                    }}
                    required
                    type="number"
                    value={Number.isFinite(urx) ? urx : ''}
                  />
                </div>
                <div className="form-field">
                  <label htmlFor="pades-ury">Upper-right Y (ury)</label>
                  <input
                    id="pades-ury"
                    onChange={(event) => {
                      markEdited('ury')
                      setUry(event.target.valueAsNumber)
                    }}
                    required
                    type="number"
                    value={Number.isFinite(ury) ? ury : ''}
                  />
                </div>
              </>
            )}
          </fieldset>
        )}

        {optionsError && <p className="field-error" role="alert">{optionsError}</p>}
        {defaultsError && (
          <p className="field-error" role="status">
            {defaultsError} Enter the values manually.
          </p>
        )}

        <button className="primary-button" disabled={!canSubmit} type="submit">
          {isSubmitting ? 'Signing document...' : 'Sign document'}
        </button>
      </form>

      {isSubmitting && <p role="status">Signing document...</p>}
      {error && (
        <div className="signing-result signing-error" role="alert">
          <p>Signing failed</p>
          <p>{error}</p>
        </div>
      )}
      {result && (
        <div className="signing-result signing-success" role="status">
          <p>Signing successful</p>
          <p>Original filename: {result.originalFileName}</p>
          <p>Resulting filename: {result.downloadFileName}</p>
          {result.requestId && <p>Operation identifier: {result.requestId}</p>}
          <a
            className="primary-button download-button"
            download={result.downloadFileName}
            href={result.downloadUrl}
          >
            Download signed document
          </a>
        </div>
      )}
    </section>
  )
}

export default SigningPage
