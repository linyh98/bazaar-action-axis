import type { ButtonHTMLAttributes, ReactNode } from 'react';

type ButtonVariant = 'primary' | 'ghost' | 'danger' | 'subtle';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  busy?: boolean;
  small?: boolean;
  children: ReactNode;
}

export function Button({ variant = 'ghost', busy, small, children, className, disabled, ...rest }: ButtonProps) {
  return (
    <button
      className={`btn btn-${variant}${small ? ' btn-small' : ''}${className ? ` ${className}` : ''}`}
      disabled={disabled || busy}
      {...rest}
    >
      {busy ? <span className="btn-spinner" aria-hidden /> : null}
      {children}
    </button>
  );
}

type BadgeTone = 'neutral' | 'gold' | 'green' | 'red' | 'blue';

export function Badge({ tone = 'neutral', children }: { tone?: BadgeTone; children: ReactNode }) {
  return <span className={`badge badge-${tone}`}>{children}</span>;
}

export function Card({ children, className }: { children: ReactNode; className?: string }) {
  return <section className={`card${className ? ` ${className}` : ''}`}>{children}</section>;
}

export function CardHeader({
  title,
  subtitle,
  actions
}: {
  title: ReactNode;
  subtitle?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <div className="card-header">
      <div>
        <h3>{title}</h3>
        {subtitle ? <p className="muted">{subtitle}</p> : null}
      </div>
      {actions ? <div className="card-actions">{actions}</div> : null}
    </div>
  );
}

export function EmptyState({
  title,
  description,
  action
}: {
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div className="empty-state">
      <div className="empty-mark" aria-hidden>
        ◇
      </div>
      <h3>{title}</h3>
      {description ? <p className="muted">{description}</p> : null}
      {action ? <div className="empty-action">{action}</div> : null}
    </div>
  );
}

export function Toggle({
  checked,
  onChange,
  label,
  description
}: {
  checked: boolean;
  onChange: (next: boolean) => void;
  label: string;
  description?: string;
}) {
  return (
    <label className="toggle-row">
      <span>
        <strong>{label}</strong>
        {description ? <span className="muted block">{description}</span> : null}
      </span>
      <span className={`switch${checked ? ' switch-on' : ''}`}>
        <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
        <span className="switch-knob" />
      </span>
    </label>
  );
}

export function Modal({
  title,
  children,
  footer,
  onClose,
  wide
}: {
  title: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  onClose?: () => void;
  wide?: boolean;
}) {
  return (
    <div className="modal-overlay" role="dialog" aria-modal>
      <div className={`modal${wide ? ' modal-wide' : ''}`}>
        <div className="modal-header">
          <h2>{title}</h2>
          {onClose ? (
            <button className="icon-btn" onClick={onClose} aria-label="关闭">
              ×
            </button>
          ) : null}
        </div>
        <div className="modal-body">{children}</div>
        {footer ? <div className="modal-footer">{footer}</div> : null}
      </div>
    </div>
  );
}

export type ToastTone = 'success' | 'error' | 'info';

export function Toast({
  tone,
  message,
  detail,
  onClose
}: {
  tone: ToastTone;
  message: string;
  detail?: string;
  onClose: () => void;
}) {
  return (
    <div className={`toast toast-${tone}`} role="status">
      <div className="toast-body">
        <strong>{message}</strong>
        {detail ? <pre className="toast-detail">{detail}</pre> : null}
      </div>
      <button className="icon-btn" onClick={onClose} aria-label="关闭通知">
        ×
      </button>
    </div>
  );
}
