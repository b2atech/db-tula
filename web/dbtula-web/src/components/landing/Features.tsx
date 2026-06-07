import React from 'react'
import { GitCompare, AlertTriangle, Wand2, History, Lock, Users } from 'lucide-react'

const Item: React.FC<{ title: string; icon: React.ElementType; children: React.ReactNode }> = ({
  title,
  icon: Icon,
  children,
}) => (
  <div className="space-y-2">
    <div className="flex items-center gap-3">
      <Icon size={36} strokeWidth={1.25} className="text-brand-orange" />
      <h3 className="text-lg font-medium text-text-primary">{title}</h3>
    </div>
    <p className="text-text-secondary">{children}</p>
  </div>
)

export const Features: React.FC = () => (
  <section id="features" className="py-14">
    <div className="mx-auto max-w-6xl px-4">
      <div className="grid gap-10 sm:grid-cols-2 lg:grid-cols-3">
        <Item title="Schema comparison" icon={GitCompare}>
          Diff tables, columns, indexes, constraints, and stored routines between any two databases.
        </Item>
        <Item title="Drift detection" icon={AlertTriangle}>
          See exactly what changed between environments — added, missing, or mismatched objects.
        </Item>
        <Item title="Sync planner" icon={Wand2}>
          Generate the SQL to close the gap, grouped into Safe, Risky, and Destructive changes.
        </Item>
        <Item title="Run history &amp; trends" icon={History}>
          Every comparison is saved, so you can track drift over time across services.
        </Item>
        <Item title="Read-only &amp; safe" icon={Lock}>
          Inspects your schemas without ever writing to your databases.
        </Item>
        <Item title="Role-based access" icon={Users}>
          Viewer and Admin roles via Google SSO with an email allowlist.
        </Item>
      </div>
    </div>
  </section>
)

export default Features
