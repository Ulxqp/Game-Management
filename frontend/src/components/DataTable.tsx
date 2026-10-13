import { flexRender, getCoreRowModel, getFilteredRowModel, getSortedRowModel, useReactTable, type ColumnDef, type SortingState } from "@tanstack/react-table";
import { ChevronsUpDown, Search } from "lucide-react";
import { useState } from "react";
import { inputClass } from "./ui";

export function DataTable<T>({ columns, data, searchPlaceholder = "Search" }: { columns: ColumnDef<T>[]; data: T[]; searchPlaceholder?: string }) {
  const [filter, setFilter] = useState("");
  const [sorting, setSorting] = useState<SortingState>([]);
  const table = useReactTable({ data, columns, state: { globalFilter: filter, sorting }, onGlobalFilterChange: setFilter, onSortingChange: setSorting, getCoreRowModel: getCoreRowModel(), getFilteredRowModel: getFilteredRowModel(), getSortedRowModel: getSortedRowModel() });
  return <div className="grid gap-3">
    <label className="relative block max-w-[280px]"><Search className="absolute left-3 top-2.5 text-[#94a3b8]" size={16}/><input aria-label={searchPlaceholder} value={filter} onChange={e => setFilter(e.target.value)} placeholder={searchPlaceholder} className={`${inputClass} pl-9`}/></label>
    <div className="overflow-x-auto rounded-[10px] border border-[#e2e8f0]"><table className="w-full min-w-[620px] border-collapse text-left text-[13px]"><thead className="h-9 bg-[#f1f5f9] text-xs font-semibold text-[#475569]">{table.getHeaderGroups().map(group => <tr key={group.id}>{group.headers.map(header => <th className="px-3" key={header.id}>{header.isPlaceholder ? null : header.column.getCanSort() ? <button type="button" onClick={header.column.getToggleSortingHandler()} className="inline-flex items-center gap-1 rounded py-1 outline-none focus-visible:ring-2 focus-visible:ring-[#93c5fd]">{flexRender(header.column.columnDef.header, header.getContext())}<ChevronsUpDown size={12} aria-hidden="true"/></button> : flexRender(header.column.columnDef.header, header.getContext())}</th>)}</tr>)}</thead><tbody>{table.getRowModel().rows.length ? table.getRowModel().rows.map(row => <tr className="h-14 border-t border-[#edf2f7] hover:bg-[#f8fafc]" key={row.id}>{row.getVisibleCells().map(cell => <td className="px-3" key={cell.id}>{flexRender(cell.column.columnDef.cell, cell.getContext())}</td>)}</tr>) : <tr><td colSpan={columns.length} className="h-24 text-center text-[#64748b]">No matching records.</td></tr>}</tbody></table></div>
  </div>;
}
