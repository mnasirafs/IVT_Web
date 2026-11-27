// Simple CSV export for a table with id 'tradingTable'
function exportTableToCSV(filename) {
    var csv = [];
    var rows = document.querySelectorAll("#tradingTable tr");

    for (var i = 0; i < rows.length; i++) {
        var row = [], cols = rows[i].querySelectorAll("td, th");
        for (var j = 0; j < cols.length; j++) {
            // skip hidden columns
            if (cols[j].offsetParent === null) continue;
            row.push('"' + cols[j].innerText.replace(/"/g, '""') + '"');
        }
        csv.push(row.join(","));
    }

    // Download CSV
    var csvFile = new Blob([csv.join("\n")], { type: "text/csv" });
    var downloadLink = document.createElement("a");
    downloadLink.download = filename;
    downloadLink.href = window.URL.createObjectURL(csvFile);
    downloadLink.style.display = "none";
    document.body.appendChild(downloadLink);
    downloadLink.click();
}

// Simple column toggle using checkboxes with data-col-index attributes
function toggleColumn(colIndex, checkbox) {
    var table = document.getElementById('tradingTable');
    if (!table) return;
    var rows = table.querySelectorAll('tr');
    rows.forEach(function (r) {
        var cells = r.querySelectorAll('th, td');
        if (cells[colIndex]) {
            cells[colIndex].style.display = checkbox.checked ? '' : 'none';
        }
    });
}

// Simple client-side pager
var currentPage = 1;
var rowsPerPage = 5;
function showPage(page) {
    var table = document.getElementById('tradingTable');
    if (!table) return;
    var rows = Array.from(table.querySelectorAll('tbody tr'));
    var totalPages = Math.ceil(rows.length / rowsPerPage) || 1;
    if (page < 1) page = 1;
    if (page > totalPages) page = totalPages;
    currentPage = page;
    var start = (page - 1) * rowsPerPage;
    var end = start + rowsPerPage;
    rows.forEach(function (r, idx) {
        r.style.display = (idx >= start && idx < end) ? '' : 'none';
    });
    // update pager UI
    var pager = document.getElementById('dashboardPager');
    if (!pager) return;
    pager.innerHTML = '';
    for (var i = 1; i <= totalPages; i++) {
        var span = document.createElement('button');
        span.className = 'page btn';
        if (i === page) span.className += ' active';
        span.innerText = i;
        (function (p) { span.addEventListener('click', function () { showPage(p); }); })(i);
        pager.appendChild(span);
    }
}

// init default
document.addEventListener('DOMContentLoaded', function () {
    // show first page
    showPage(1);
});
