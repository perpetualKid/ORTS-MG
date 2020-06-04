// COPYRIGHT 2009, 2010, 2011, 2012, 2013, 2014 by the Open Rails project.
//
// This file is part of Open Rails.
//
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.
//
// Based on original work by Dan Reynolds 2017-12-21

// Using XMLHttpRequest rather than fetch() as:
// 1. it is more widely supported (e.g. Internet Explorer and various tablets)
// 2. It doesn't hide some returning error codes
// 3. We don't need the ability to chain promises that fetch() offers.

var hr = new XMLHttpRequest;
var httpCodeSuccess = 200;
var xmlHttpRequestCodeDone = 4;

var idleMs = 500; // default idle time between calls
function poll(initialIdleMs) {
    if (initialIdleMs != null)
        idleMs = initialIdleMs; // Save it to use at end

    api();

    // setTimeout() used instead of setInterval() to avoid overloading the browser's queue.
    // (It's not true recursion, so it won't blow the stack.)
    setTimeout(poll, idleMs); // In this call, initialIdleMs == null
}

var pageNo = 0;

function api() {
    // If this file is located in folder /API/<API_name>/, then Open Rails will call the API with the signature "/API/<API_name"

    // GET preferred over POST as Internet Explorer may then fail intermittently with network error 00002eff
    // hr.open("post", "call_API", true);
    // hr.send("pageno="+pageNo);
    hr.open("GET", "call_API?pageNo=" + pageNo, true);
    hr.send();

    hr.onreadystatechange = function () {
        if (this.readyState == xmlHttpRequestCodeDone && this.status == httpCodeSuccess) {
            var obj = JSON.parse(hr.responseText);
            if (obj != null) // Can happen using IEv11
            {
                var rows = obj.commonTable.nRows;
                var cols = obj.commonTable.nCols;
                Str = "<table>";
                var next = 0;
                for (var row = 0; row < obj.commonTable.nRows; ++row) {
                    Str += "<tr>";
                    for (var col = 0; col < obj.commonTable.nCols; ++col) {
                        if (obj.commonTable.values[next] == null) {
                            Str += "<td></td>";
                        }
                        else if (obj.commonTable.values[next].slice(-3) == "???"
                            || obj.commonTable.values[next].slice(-3) == "!!!"
                            || obj.commonTable.values[next].slice(-3) == "%%%") {
                            var endIndex = obj.commonTable.values[next].length;
                            var newData = obj.commonTable.values[next].slice(0, endIndex - 3);
                            Str += "<td>" + newData + "</td>";
                        }
                        else {
                            Str += "<td>" + obj.commonTable.values[next] + "</td>";
                        }
                        ++next;
                    }
                    Str += "</tr>";
                }
                Str += "</table>";
                HUDCommon.innerHTML = Str;

                if (obj.nTables == 2) {
                    var rows = obj.extraTable.nRows;
                    var cols = obj.extraTable.nCols;
                    next = 0;
                    Str = "<table>";
                    for (var row = 0; row < obj.extraTable.nRows; ++row) {
                        Str += "<tr>";
                        for (var col = 0; col < obj.extraTable.nCols; ++col) {
                            if (obj.extraTable.values[next] == null) {
                                Str += "<td></td>";
                            }
                            else if (obj.extraTable.values[next].slice(-3) == "???"
                                || obj.extraTable.values[next].slice(-3) == "!!!"
                                || obj.extraTable.values[next].slice(-3) == "%%%") {
                                var endIndex = obj.extraTable.values[next].length;
                                var newData = obj.extraTable.values[next].slice(0, endIndex - 3);
                                Str += "<td>" + newData + "</td>";
                            }
                            else {
                                Str += "<td>" + obj.extraTable.values[next] + "</td>";
                            }
                            ++next;
                        }
                        Str += "</tr>";
                    }
                    Str += "</table>";
                    HUDExtra.innerHTML = Str;
                }
            }
        }
    }
}