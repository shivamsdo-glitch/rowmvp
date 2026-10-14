import { cn } from '@/lib/cn'

interface FormFieldProps {
  label: string
  /** Wires label `htmlFor` and the child input's `id` for accessibility. */
  id?: string
  required?: boolean
  hint?: string
  error?: string
  children: React.ReactNode
  className?: string
}

export function FormField({ label, id, required, hint, error, children, className }: FormFieldProps) {
  return (
    <div className={cn('textbox', className)}>
      <div className="top-section medium">
        <label htmlFor={id} className={required ? 'label-enabled' : 'label-default'}>
          {label}
        </label>
      </div>
      <div className="text-box-styles medium">
        <div className={cn(
          'e-input-group e-control-wrapper e-outline',
          error ? 'error' : '',
        )}>
          {children}
        </div>
      </div>
      {hint && !error && (
        <p className="text-[10px] text-muted-foreground mt-1">{hint}</p>
      )}
      {error && (
        <div className="validation-text error">
          <p className="text-[10px] text-danger mt-1">{error}</p>
        </div>
      )}
    </div>
  )
}
