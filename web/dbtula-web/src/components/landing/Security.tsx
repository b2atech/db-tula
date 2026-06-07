import React from 'react'
import { ShieldCheck } from 'lucide-react'

export const Security: React.FC = () => (
  <section id="security" className="py-14">
    <div className="mx-auto grid max-w-6xl items-center gap-10 px-4 lg:grid-cols-2">
      <div>
        <h2 className="text-2xl font-semibold text-text-primary">Security &amp; read-only guarantees</h2>
        <p className="mt-2 text-text-secondary">
          db-tula reads your schemas and never writes to them. Access is gated by Google SSO with
          an email allowlist, and every comparison is recorded.
        </p>
        <ul className="mt-4 space-y-2 text-sm text-text-muted">
          <li>• Read-only connectors — never writes to your databases</li>
          <li>• Google SSO with an email allowlist</li>
          <li>• Run history and a record of every applied statement</li>
        </ul>
      </div>
      <div className="flex items-center justify-center">
        <ShieldCheck size={72} strokeWidth={1.5} className="text-brand-orange" />
      </div>
    </div>
  </section>
)

export default Security
