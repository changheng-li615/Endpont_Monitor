import Link from "next/link";
import {
  buildDeviceDetailHref,
  paginationItems,
  type Pagination,
  type QueryValues,
} from "@/lib/device-detail-view";

type PaginationNavProps = {
  deviceId: string;
  query: QueryValues;
  pagination: Pagination;
  pageParameter: string;
  pageSizeParameter: string;
  pageSizes: readonly number[];
  label: string;
};

export function PaginationNav({
  deviceId,
  query,
  pagination,
  pageParameter,
  pageSizeParameter,
  pageSizes,
  label,
}: PaginationNavProps) {
  const hrefForPage = (page: number) => buildDeviceDetailHref(deviceId, query, { [pageParameter]: page });
  return (
    <div className="pagination-row">
      <p className="pagination-count">
        Showing {pagination.firstItem}-{pagination.lastItem} of {pagination.totalItems}
      </p>
      <nav className="pagination" aria-label={label}>
        {pagination.page > 1 ? <Link href={hrefForPage(pagination.page - 1)}>Previous</Link> : <span className="disabled">Previous</span>}
        {paginationItems(pagination.page, pagination.totalPages).map((item, index) =>
          item === "ellipsis" ? (
            <span className="ellipsis" key={`ellipsis-${index}`} aria-hidden="true">...</span>
          ) : (
            <Link
              key={item}
              href={hrefForPage(item)}
              className={item === pagination.page ? "active" : undefined}
              aria-current={item === pagination.page ? "page" : undefined}
            >
              {item}
            </Link>
          ),
        )}
        {pagination.page < pagination.totalPages ? <Link href={hrefForPage(pagination.page + 1)}>Next</Link> : <span className="disabled">Next</span>}
      </nav>
      <div className="page-sizes" aria-label={`${label} page size`}>
        <span>Per page:</span>
        {pageSizes.map((size) => (
          <Link
            key={size}
            href={buildDeviceDetailHref(deviceId, query, { [pageParameter]: 1, [pageSizeParameter]: size })}
            className={size === pagination.pageSize ? "active" : undefined}
            aria-current={size === pagination.pageSize ? "true" : undefined}
          >
            {size}
          </Link>
        ))}
      </div>
    </div>
  );
}
